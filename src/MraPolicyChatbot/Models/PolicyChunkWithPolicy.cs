namespace MraPolicyChatbot.Models;

// Phase 6: read-only shape returned by
// PolicyChunkRepository.GetAllWithEmbeddingsAsync, pairing a chunk with its
// parent policy's title so ChatService can both score it for retrieval and
// cite the source policy in the final answer. Not mapped to any single
// table on its own.
public class PolicyChunkWithPolicy
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public string PolicyTitle { get; set; } = string.Empty;
    public string ChunkText { get; set; } = string.Empty;
    public string Embedding { get; set; } = string.Empty;
}
