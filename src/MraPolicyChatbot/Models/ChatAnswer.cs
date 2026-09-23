namespace MraPolicyChatbot.Models;

// Phase 6: result of a single chatbot Q&A turn, returned by ChatService and
// serialized as JSON by ChatController.Ask. Not persisted anywhere yet —
// query logging/persistence is Phase 7's QueryLogs table, not this phase.
public class ChatAnswer
{
    public string Text { get; set; } = string.Empty;

    // Distinct titles of the policies the answer was drawn from. Empty when
    // no confident match was found (the "no answer" case) or when Ollama
    // was unreachable.
    public List<string> Sources { get; set; } = new();
}
