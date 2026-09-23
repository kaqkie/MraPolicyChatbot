namespace MraPolicyChatbot.Models;

// Phase 4: display model for a row in the admin policy list. Mapped from
// the real Policy/dbo.Policies data (Phase 1 used a hardcoded mock version
// of this same class). Keeping this separate from Policy avoids exposing
// FilePath/ContentText to the view and gives later phases (e.g. Phase 5's
// processing status/chunk count) a clean place to add display-only fields.
public class PolicyListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateOnly UploadDate { get; set; }
    public bool IsActive { get; set; }
    public bool HasFile { get; set; }

    // Phase 5: text extraction / chunking pipeline status.
    public bool IsProcessed { get; set; }
    public string? ProcessingError { get; set; }
    public int ChunkCount { get; set; }
}
