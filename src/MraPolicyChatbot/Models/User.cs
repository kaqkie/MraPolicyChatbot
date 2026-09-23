namespace MraPolicyChatbot.Models;

// Phase 2: plain POCO mapped by Dapper to/from the dbo.Users table.
// No Entity Framework attributes, no validation, no business logic.
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; }
}
