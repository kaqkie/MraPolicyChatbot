namespace MraPolicyChatbot.Models;

// Phase 2: plain POCO mapped by Dapper to/from the dbo.Policies table.
// No Entity Framework attributes, no validation, no business logic.
public class Policy
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? ContentText { get; set; }
    public int? UploadedBy { get; set; }
    public DateTime UploadDate { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }

    // Phase 5: text extraction / chunking pipeline status.
    public bool IsProcessed { get; set; }
    public string? ProcessingError { get; set; }
}
