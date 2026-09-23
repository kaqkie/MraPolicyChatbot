namespace MraPolicyChatbot.Models;

// Phase 2: plain POCO mapped by Dapper to/from the dbo.AuditLog table.
// No Entity Framework attributes, no validation, no business logic.
public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}
