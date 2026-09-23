using Microsoft.AspNetCore.Mvc;
using MraPolicyChatbot.Filters;
using MraPolicyChatbot.Services;

namespace MraPolicyChatbot.Controllers;

// Phase 1: UI shell only (superseded — the conversation was a hardcoded mock).
// Phase 3: requires a logged-in session (any role — Admin or Employee).
// Phase 6: Index now renders an empty chat shell; Ask is the real
// AJAX-driven question-answering endpoint backed by ChatService. No query
// persistence/logging yet — that is Phase 7's QueryLogs table.
// Speed pass: Ask now streams its answer back as it's generated (NDJSON —
// one small JSON object per line) rather than waiting for the whole
// answer and returning one JSON blob. This is what lets the Chat page
// show/speak the first part of an answer immediately instead of after the
// entire generation finishes, on hardware where generation itself can't
// be made faster.
[Authorize]
public class ChatController : Controller
{
    private readonly ChatService _chatService;

    public ChatController(ChatService chatService)
    {
        _chatService = chatService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task Ask([FromBody] AskRequest? request)
    {
        // application/x-ndjson (not application/json — the body isn't one
        // JSON value, it's a sequence of them) so nothing downstream tries
        // to buffer/parse it as a single object.
        Response.ContentType = "application/x-ndjson";
        // Some reverse proxies (nginx in particular) buffer streamed
        // responses by default unless told not to, which would silently
        // undo the whole point of streaming. Harmless if there's no proxy
        // in front of Kestrel, which is the common case for this app.
        Response.Headers["X-Accel-Buffering"] = "no";

        if (request is null || string.IsNullOrWhiteSpace(request.Question))
        {
            await WriteLineAsync(new { type = "token", text = "Question is required." });
            await WriteLineAsync(new { type = "done" });
            return;
        }

        var sources = await _chatService.AskStreamAsync(
            request.Question.Trim(),
            chunk => WriteLineAsync(new { type = "token", text = chunk }));

        await WriteLineAsync(new { type = "sources", sources });
        await WriteLineAsync(new { type = "done" });
    }

    private async Task WriteLineAsync(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        await Response.WriteAsync(json + "\n");
        await Response.Body.FlushAsync();
    }

    public class AskRequest
    {
        public string Question { get; set; } = string.Empty;
    }
}
