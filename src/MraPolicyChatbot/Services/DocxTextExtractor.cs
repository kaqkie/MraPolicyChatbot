using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MraPolicyChatbot.Services;

// Phase 5: extracts plain text from a DOCX using the OpenXML SDK
// (Microsoft, free, no cloud/AI dependency).
public class DocxTextExtractor
{
    private readonly ILogger<DocxTextExtractor> _logger;

    public DocxTextExtractor(ILogger<DocxTextExtractor> logger)
    {
        _logger = logger;
    }

    public string ExtractText(string filePath)
    {
        try
        {
            using var document = WordprocessingDocument.Open(filePath, isEditable: false);
            var body = document.MainDocumentPart?.Document.Body;
            if (body is null)
            {
                return string.Empty;
            }

            // Join paragraph text with blank lines between paragraphs so
            // paragraph boundaries survive for TextChunker. (Body.InnerText
            // alone concatenates everything with no separators.)
            var paragraphs = body.Elements<Paragraph>()
                .Select(p => p.InnerText)
                .Where(t => !string.IsNullOrWhiteSpace(t));

            return string.Join("\n\n", paragraphs);
        }
        catch (Exception ex)
        {
            // Covers corrupt files, password-protected DOCX (the OpenXML
            // SDK throws when it can't open the package), and unsupported
            // formats (e.g. a legacy .doc renamed to .docx).
            _logger.LogError(ex, "Failed to extract text from DOCX at {FilePath}", filePath);
            return string.Empty;
        }
    }
}
