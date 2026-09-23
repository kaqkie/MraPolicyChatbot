using System.Text.Json;
using MraPolicyChatbot.Data;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Services;

// Phase 6: retrieval-augmented Q&A over approved, processed policy chunks.
// Retrieval is brute-force in-memory cosine similarity over every chunk
// with a stored embedding — no vector database, no full-text search index,
// consistent with the project's local/free tooling constraint and the
// small expected document set. No query persistence/logging here — that is
// Phase 7's QueryLogs table, deliberately not built ahead of schedule.
public class ChatService
{
    // Below this cosine similarity score, the best match is treated as "not
    // a real match" and the LLM is never called — this avoids sending
    // irrelevant context to the model and reduces the chance of a
    // hallucinated answer. Admin-editable from Admin > Settings
    // (dbo.AppSettings, key "Chat:SimilarityThreshold"); this constant is
    // now only the fallback used if that setting is missing/unreadable.
    private const double DefaultSimilarityThreshold = 0.35;
    // Was 4: each retrieved chunk is ~400 words, so 4 of them means the
    // model has to process up to ~1,600 words of context before it even
    // starts generating a word of the answer. On CPU-only hardware
    // (confirmed slow even for a trivial one-line prompt run directly
    // through Ollama, outside this app entirely) that prefill cost is a
    // large chunk of the total wait. Dropping to 3 meaningfully shrinks
    // that cost; it's a real speed/recall trade-off, not free, so if
    // answers start missing detail that used to be there, that's the
    // knob to nudge back up.
    private const int TopK = 3;

    // See the retry logic in AskAsync: a failed call that came back in
    // under this many milliseconds is treated as a fast connection-level
    // hiccup worth retrying once; a failure that took longer than this
    // means the timeout itself was likely reached, so retrying would just
    // double an already-long wait for no benefit.
    private const int FastFailureRetryThresholdMs = 5000;

    private const string NoMatchAnswer =
        "I don't have information about that in the approved policy documents. " +
        "Try rephrasing your question, or check with your manager or HR if you believe this should be covered.";

    private const string UnavailableAnswer =
        "The chatbot is temporarily unavailable. Please try again shortly or contact an admin.";

    private readonly PolicyChunkRepository _chunkRepository;
    private readonly OllamaClient _ollamaClient;
    private readonly AppSettingsRepository _settingsRepository;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        PolicyChunkRepository chunkRepository,
        OllamaClient ollamaClient,
        AppSettingsRepository settingsRepository,
        ILogger<ChatService> logger)
    {
        _chunkRepository = chunkRepository;
        _ollamaClient = ollamaClient;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task<ChatAnswer> AskAsync(string question)
    {
        var questionEmbedding = await _ollamaClient.GetEmbeddingAsync(question);
        if (questionEmbedding is null)
        {
            _logger.LogWarning("ChatService.AskAsync: could not get an embedding for the question (Ollama unreachable?).");
            return new ChatAnswer { Text = UnavailableAnswer };
        }

        var allChunks = await _chunkRepository.GetAllWithEmbeddingsAsync();

        var scored = new List<(PolicyChunkWithPolicy Chunk, double Score)>();
        foreach (var chunk in allChunks)
        {
            var chunkEmbedding = DeserializeEmbedding(chunk.Embedding);
            if (chunkEmbedding is null || chunkEmbedding.Length != questionEmbedding.Length)
            {
                continue;
            }

            scored.Add((chunk, CosineSimilarity(questionEmbedding, chunkEmbedding)));
        }

        var topMatches = scored.OrderByDescending(s => s.Score).Take(TopK).ToList();
        var similarityThreshold = await GetSimilarityThresholdAsync();

        if (topMatches.Count == 0 || topMatches[0].Score < similarityThreshold)
        {
            return new ChatAnswer { Text = NoMatchAnswer };
        }

        var context = string.Join(
            "\n\n---\n\n",
            topMatches.Select(m => $"From \"{m.Chunk.PolicyTitle}\":\n{m.Chunk.ChunkText}"));

        var prompt =
            "You are an internal assistant for MRA employees. Answer the question using ONLY the " +
            "policy excerpts below. Do not use any outside knowledge, and do not make anything up.\n\n" +
            "The employee's question may be informally phrased, misspelled, abbreviated, or not " +
            "worded quite correctly (e.g. \"study leave pay\" instead of \"paid study leave\", or " +
            "\"PIP\" instead of \"Performance Improvement Plan\"). Interpret it charitably: work out " +
            "which real policy topic in the excerpts it is most likely asking about, and answer that " +
            "question rather than rejecting it over wording.\n\n" +
            "If, after that, the excerpts genuinely do not contain the answer, say clearly that you " +
            "don't have that information in the approved policy documents rather than guessing.\n\n" +
            $"Policy excerpts:\n{context}\n\n" +
            $"Question: {question}\n\n" +
            "Answer:";

        // One retry on a null result before giving up — but only if the
        // first attempt failed FAST (a quick connection-level hiccup).
        // Blindly retrying after a null caused a worse regression than the
        // problem it was meant to fix: if the first call already burned
        // its full ~180s timeout (a genuinely slow/stuck generation, not a
        // transient blip), retrying gave it ANOTHER full ~180s before
        // giving up — up to 3 minutes of "Thinking..." instead of the
        // previous 180s cap. Now only retries when the failure came back
        // quickly, which is what a dropped connection/one-off hiccup looks
        // like; a slow timeout is left alone and reported as unavailable
        // right away instead of doubling the wait.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var answerText = await _ollamaClient.GenerateAnswerAsync(prompt);
        stopwatch.Stop();

        if (answerText is null && stopwatch.ElapsedMilliseconds < FastFailureRetryThresholdMs)
        {
            answerText = await _ollamaClient.GenerateAnswerAsync(prompt);
        }

        if (answerText is null)
        {
            _logger.LogWarning("ChatService.AskAsync: could not get a generated answer (Ollama unreachable, or too slow to respond?).");
            return new ChatAnswer { Text = UnavailableAnswer };
        }

        var sources = topMatches.Select(m => m.Chunk.PolicyTitle).Distinct().ToList();

        return new ChatAnswer { Text = answerText.Trim(), Sources = sources };
    }

    // Streaming counterpart to AskAsync: identical retrieval/no-match/
    // threshold logic, but the answer is delivered to onToken piece by
    // piece as Ollama produces it, instead of being returned all at once.
    // Returns the matched sources once streaming completes, or null for
    // the no-match/unavailable cases (there's nothing to cite).
    //
    // Retry logic here is simpler AND safer than AskAsync's stopwatch-based
    // version: streaming makes "did anything at all come back" directly
    // observable via receivedAnyToken, so there's no need to guess from
    // elapsed time whether a failure was a fast connection hiccup or a
    // slow timeout — if we received zero tokens, the failure was
    // necessarily fast (a real answer streams its first token well before
    // the overall timeout), so retrying is always safe here.
    public async Task<List<string>?> AskStreamAsync(string question, Func<string, Task> onToken)
    {
        var questionEmbedding = await _ollamaClient.GetEmbeddingAsync(question);
        if (questionEmbedding is null)
        {
            _logger.LogWarning("ChatService.AskStreamAsync: could not get an embedding for the question (Ollama unreachable?).");
            await onToken(UnavailableAnswer);
            return null;
        }

        var allChunks = await _chunkRepository.GetAllWithEmbeddingsAsync();

        var scored = new List<(PolicyChunkWithPolicy Chunk, double Score)>();
        foreach (var chunk in allChunks)
        {
            var chunkEmbedding = DeserializeEmbedding(chunk.Embedding);
            if (chunkEmbedding is null || chunkEmbedding.Length != questionEmbedding.Length)
            {
                continue;
            }

            scored.Add((chunk, CosineSimilarity(questionEmbedding, chunkEmbedding)));
        }

        var topMatches = scored.OrderByDescending(s => s.Score).Take(TopK).ToList();
        var similarityThreshold = await GetSimilarityThresholdAsync();

        if (topMatches.Count == 0 || topMatches[0].Score < similarityThreshold)
        {
            await onToken(NoMatchAnswer);
            return null;
        }

        var context = string.Join(
            "\n\n---\n\n",
            topMatches.Select(m => $"From \"{m.Chunk.PolicyTitle}\":\n{m.Chunk.ChunkText}"));

        var prompt =
            "You are an internal assistant for MRA employees. Answer the question using ONLY the " +
            "policy excerpts below. Do not use any outside knowledge, and do not make anything up.\n\n" +
            "The employee's question may be informally phrased, misspelled, abbreviated, or not " +
            "worded quite correctly (e.g. \"study leave pay\" instead of \"paid study leave\", or " +
            "\"PIP\" instead of \"Performance Improvement Plan\"). Interpret it charitably: work out " +
            "which real policy topic in the excerpts it is most likely asking about, and answer that " +
            "question rather than rejecting it over wording.\n\n" +
            "If, after that, the excerpts genuinely do not contain the answer, say clearly that you " +
            "don't have that information in the approved policy documents rather than guessing.\n\n" +
            $"Policy excerpts:\n{context}\n\n" +
            $"Question: {question}\n\n" +
            "Answer:";

        var receivedAnyToken = false;
        Func<string, Task> trackedOnToken = async chunk =>
        {
            receivedAnyToken = true;
            await onToken(chunk);
        };

        var streamedOk = await _ollamaClient.GenerateAnswerStreamAsync(prompt, trackedOnToken);

        if (!streamedOk && !receivedAnyToken)
        {
            // Nothing arrived at all — genuinely safe to retry once (see
            // the method comment above for why this differs from AskAsync's
            // elapsed-time guess).
            streamedOk = await _ollamaClient.GenerateAnswerStreamAsync(prompt, trackedOnToken);
        }

        if (!receivedAnyToken)
        {
            _logger.LogWarning("ChatService.AskStreamAsync: no tokens were streamed back after a retry (Ollama unreachable?).");
            await onToken(UnavailableAnswer);
            return null;
        }

        return topMatches.Select(m => m.Chunk.PolicyTitle).Distinct().ToList();
    }

    // Never throws: a transient database hiccup while reading this one
    // setting should degrade to the built-in default, not break the whole
    // question — same reasoning as OllamaClient's own settings lookups.
    private async Task<double> GetSimilarityThresholdAsync()
    {
        try
        {
            var raw = await _settingsRepository.GetValueAsync(AppSettingsRepository.SimilarityThresholdKey);
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed is >= 0 and <= 1)
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {SettingKey} from settings; using the default threshold.", AppSettingsRepository.SimilarityThresholdKey);
        }

        return DefaultSimilarityThreshold;
    }

    private static float[]? DeserializeEmbedding(string embeddingJson)
    {
        try
        {
            return JsonSerializer.Deserialize<float[]>(embeddingJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
