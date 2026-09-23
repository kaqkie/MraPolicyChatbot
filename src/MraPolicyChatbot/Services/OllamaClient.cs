using System.Net.Http.Json;
using MraPolicyChatbot.Data;

namespace MraPolicyChatbot.Services;

// Phase 6: thin HTTP wrapper over a locally-running Ollama server
// (https://ollama.com) — no cloud AI API dependency.
//
// The base URL and model names are admin-editable at runtime from the
// Admin > Settings page (backed by AppSettingsRepository/dbo.AppSettings),
// so every call re-reads them fresh rather than caching them once in the
// constructor — that's also why this builds a full absolute URI per call
// instead of setting HttpClient.BaseAddress once: BaseAddress is shared,
// mutable state on the HttpClient instance, and mutating it per-request
// would be a race condition under concurrent requests. If the database
// has no value yet (a fresh install, or the table predates this feature),
// each setting falls back to the matching "Ollama" section of
// appsettings.json, then to a standard local-install default.
//
// Uses System.Net.Http.Json (PostAsJsonAsync/ReadFromJsonAsync), which is
// part of the .NET 8 shared framework — this class needs no new NuGet
// package.
//
// Every call degrades gracefully: on any failure to reach Ollama (not
// running, wrong URL, timeout) or a non-success HTTP status, the method
// logs a warning and returns null rather than throwing, so callers (chunk
// processing, chat Q&A) can decide how to handle "AI unavailable" without
// crashing.
public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly AppSettingsRepository _settingsRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(
        HttpClient httpClient,
        AppSettingsRepository settingsRepository,
        IConfiguration configuration,
        ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _settingsRepository = settingsRepository;
        _configuration = configuration;
        _logger = logger;

        // This was 30 seconds, which is fine for GetEmbeddingAsync (near-
        // instant) but was cutting off GenerateAnswerAsync mid-inference:
        // a local LLM answering from a RAG prompt (policy excerpts + the
        // question) can legitimately take well over 30 seconds on ordinary
        // laptop hardware, especially for the first request after Ollama
        // loads the model into memory. When that happened, the request was
        // silently cancelled and the chatbot returned "temporarily
        // unavailable" even though Ollama was still working correctly —
        // it just hadn't finished yet. Raised once already (30s -> 120s);
        // raised again here to 180s after the same symptom recurred, since
        // a cold model load on CPU-only hardware (no GPU) can still exceed
        // 120s for the very first generate call. A single shared timeout is
        // fine here since it's an upper bound, not a fixed delay: embeddings
        // still return in well under a second regardless of how high this
        // is set. If "temporarily unavailable" keeps happening even after
        // this, check the console output for the specific warning logged
        // below (timeout vs. connection-refused vs. bad HTTP status all log
        // differently) — that pinpoints the real cause rather than guessing.
        _httpClient.Timeout = TimeSpan.FromSeconds(180);
    }

    public async Task<float[]?> GetEmbeddingAsync(string text)
    {
        var baseUrl = await GetBaseUrlAsync();
        var model = await GetSettingAsync(AppSettingsRepository.OllamaEmbeddingModelKey, "Ollama:EmbeddingModel", "nomic-embed-text");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildUri(baseUrl, "/api/embeddings"), new
            {
                model,
                prompt = text,
                // Keeps this model resident in Ollama's memory for 30
                // minutes after each call instead of Ollama's own default
                // (5 minutes), which was causing every question after a
                // short gap to pay the multi-second "load model back into
                // memory" cost again on top of actually answering — the
                // main cause of the chatbot feeling slow. Doesn't change
                // answer quality, just avoids repeated reload cost.
                keep_alive = "30m",
            });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama embeddings call to {BaseUrl} failed with status {StatusCode}.",
                    baseUrl, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
            return result?.Embedding;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger.LogWarning(ex, "Ollama embeddings call could not reach {BaseUrl}.", baseUrl);
            return null;
        }
    }

    public async Task<string?> GenerateAnswerAsync(string prompt)
    {
        var baseUrl = await GetBaseUrlAsync();
        var model = await GetSettingAsync(AppSettingsRepository.OllamaChatModelKey, "Ollama:ChatModel", "llama3.2");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildUri(baseUrl, "/api/generate"), new
            {
                model,
                prompt,
                stream = false,
                // Same reasoning as the embeddings call above — keeps the
                // (usually larger, slower-to-load) chat model resident
                // between questions instead of it being evicted after 5
                // idle minutes and having to reload on the next question.
                keep_alive = "30m",
                options = new
                {
                    // Was 350. Every generated token costs real wall-clock
                    // time on CPU-only hardware (confirmed slow directly
                    // via `ollama run`, independent of this app) — capping
                    // shorter caps the generation half of the wait, on top
                    // of the TopK reduction above shrinking the prefill
                    // half. 220 is still enough for a solid paragraph
                    // answer; it just won't ramble.
                    num_predict = 220,
                },
            });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama generate call to {BaseUrl} failed with status {StatusCode}.",
                    baseUrl, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
            return result?.Response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger.LogWarning(ex, "Ollama generate call could not reach {BaseUrl}.", baseUrl);
            return null;
        }
    }

    // Streaming counterpart to GenerateAnswerAsync above — same model,
    // same options, but stream=true and onToken is invoked once per token
    // (or small token group) as Ollama produces it, instead of waiting for
    // the whole answer before returning anything.
    //
    // This exists purely to fix PERCEIVED speed, not actual compute time:
    // the model still takes exactly as long to finish the full answer on
    // this hardware. What changes is when the person first sees/hears
    // something — the moment the model emits its first token, rather than
    // after it has finished emitting every token. For a locally-hosted
    // model on CPU-only hardware, that first-token wait is still real (it's
    // roughly the prompt-processing/prefill time), but everything after it
    // now overlaps with reading/speaking instead of happening invisibly
    // before anything is shown.
    //
    // Returns true if the stream reached a normal "done" from Ollama, false
    // on any connection/HTTP-level failure. Whether any tokens were
    // actually received is up to the caller to track via onToken — that
    // distinction is what lets ChatService tell "failed immediately" apart
    // from "failed partway through a real answer" without this class
    // needing to know anything about retry policy.
    // System.Text.Json.JsonSerializer.Deserialize with no options is
    // case-SENSITIVE by default. Ollama's streaming NDJSON uses lowercase
    // keys ("response", "done"); OllamaGenerateResponse's properties are
    // PascalCase to match .NET convention. Without this, every line would
    // silently deserialize with Response staying null and Done staying
    // false — no exception, no log, just a stream that produces zero
    // tokens and never ends until the connection itself closes. (The
    // non-streaming ReadFromJsonAsync<T> calls elsewhere in this class
    // don't need this explicitly — that extension method applies
    // case-insensitive matching by default; raw JsonSerializer.Deserialize
    // does not.)
    private static readonly System.Text.Json.JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<bool> GenerateAnswerStreamAsync(string prompt, Func<string, Task> onToken)
    {
        var baseUrl = await GetBaseUrlAsync();
        var model = await GetSettingAsync(AppSettingsRepository.OllamaChatModelKey, "Ollama:ChatModel", "llama3.2");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, "/api/generate"))
            {
                Content = JsonContent.Create(new
                {
                    model,
                    prompt,
                    stream = true,
                    keep_alive = "30m",
                    options = new { num_predict = 220 },
                }),
            };

            // ResponseHeadersRead: without this, HttpClient buffers the
            // ENTIRE response before returning it, which would defeat the
            // whole point — we'd be back to waiting for the full answer
            // before seeing anything, just with extra NDJSON parsing on
            // top. This makes the response stream available for reading
            // as soon as the headers arrive, while Ollama is still writing
            // more of the body.
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama streaming generate call to {BaseUrl} failed with status {StatusCode}.",
                    baseUrl, response.StatusCode);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            // Ollama's streaming format is NDJSON: one complete JSON object
            // per line, each carrying the next slice of the answer in
            // "response", until a final line where "done" is true.
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OllamaGenerateResponse? chunk;
                try
                {
                    chunk = System.Text.Json.JsonSerializer.Deserialize<OllamaGenerateResponse>(line, StreamJsonOptions);
                }
                catch (System.Text.Json.JsonException)
                {
                    // A malformed/partial line shouldn't take down the
                    // whole answer — skip it and keep reading.
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk?.Response))
                {
                    await onToken(chunk.Response);
                }

                if (chunk?.Done == true)
                {
                    break;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or IOException)
        {
            _logger.LogWarning(ex, "Ollama streaming generate call could not reach {BaseUrl}.", baseUrl);
            return false;
        }
    }

    private Task<string> GetBaseUrlAsync() =>
        GetSettingAsync(AppSettingsRepository.OllamaBaseUrlKey, "Ollama:BaseUrl", "http://localhost:11434");

    // Reads an admin-configurable setting from the database, falling back
    // to appsettings.json and then a hardcoded default — and, importantly,
    // never throwing: a transient database hiccup here should degrade to
    // "use the last known/default value", not break the whole chat
    // request the way an unhandled exception would.
    private async Task<string> GetSettingAsync(string settingKey, string configKey, string fallback)
    {
        try
        {
            var dbValue = await _settingsRepository.GetValueAsync(settingKey);
            if (!string.IsNullOrWhiteSpace(dbValue))
            {
                return dbValue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read setting {SettingKey} from the database; using the configured/default value instead.", settingKey);
        }

        return _configuration[configKey] ?? fallback;
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        var normalizedBase = baseUrl.Trim();
        if (!normalizedBase.EndsWith('/'))
        {
            normalizedBase += "/";
        }

        return new Uri(new Uri(normalizedBase), path.TrimStart('/'));
    }

    private class OllamaEmbeddingResponse
    {
        public float[]? Embedding { get; set; }
    }

    private class OllamaGenerateResponse
    {
        public string? Response { get; set; }
        public bool Done { get; set; }
    }
}
