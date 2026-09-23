namespace MraPolicyChatbot.Models;

// Backs Views/Admin/Settings.cshtml. Plain data-transfer object — no
// validation attributes; AdminController checks values explicitly, same
// convention as PolicyEditViewModel.
public class AdminSettingsViewModel
{
    public string OllamaBaseUrl { get; set; } = string.Empty;
    public string OllamaEmbeddingModel { get; set; } = string.Empty;
    public string OllamaChatModel { get; set; } = string.Empty;
    public double SimilarityThreshold { get; set; }

    // One department per line, for a plain <textarea> — parsed/joined by
    // AppSettingsRepository rather than stored as a delimited single line.
    public string DepartmentList { get; set; } = string.Empty;
}
