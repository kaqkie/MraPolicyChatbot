namespace MraPolicyChatbot.Models;

// Phase 2: plain POCO mapped by Dapper to/from the dbo.PolicyChunks table.
// No Entity Framework attributes, no validation, no business logic.
//
// Embedding is a placeholder NVARCHAR(MAX) column for now — no AI/embedding
// generation exists until a later phase.
public class PolicyChunk
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public int ChunkOrder { get; set; }
    public string? Embedding { get; set; }
    public DateTime CreatedDate { get; set; }
}
