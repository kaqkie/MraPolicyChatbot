namespace MraPolicyChatbot.Models;

// Phase 4: display model for the admin Edit Policy form's Title/Category
// fields. The optional replacement file is a plain IFormFile parameter on
// AdminController.EditPolicy(POST), not part of this model.
public class PolicyEditViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
