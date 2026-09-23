using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using TesseractPage = Tesseract.Page;

namespace MraPolicyChatbot.Services;

// Phase 5: extracts plain text from a PDF using PdfPig (free, open-source,
// no cloud/AI dependency). Most uploaded PDFs have a real, selectable text
// layer and are handled entirely by PdfPig.
//
// Phase 5 add-on: some policy documents (e.g. a signed circular that was
// printed, signed, and scanned back in) have NO text layer at all — every
// page is really just one big picture of a page. PdfPig correctly reports
// that as empty text; it isn't a bug or a corrupt file. For exactly those
// pages, this class now falls back to Tesseract — a free, fully local OCR
// engine (no cloud/paid API, consistent with the project's tooling rules)
// — run against the page's own embedded image. This requires the English
// language data file at App_Data/tessdata/eng.traineddata (see that
// folder's own note); if it's missing, OCR is skipped and the page simply
// extracts as empty text, same as before this add-on existed.
public class PdfTextExtractor
{
    // Below this many characters, a page is treated as having no real text
    // layer worth using (a handful of stray characters can leak through
    // even on a fully scanned page — from a rotated page-number field, for
    // instance) and is sent through OCR instead.
    private const int MinCharsPerPageForRealText = 10;

    private readonly ILogger<PdfTextExtractor> _logger;
    private readonly string _tessDataPath;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _tessDataPath = Path.Combine(environment.ContentRootPath, "App_Data", "tessdata");
    }

    public string ExtractText(string filePath)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            var pageTexts = new List<string>();

            // The OCR engine is fairly expensive to spin up (it loads the
            // language model into memory), so it's created at most once per
            // document — only if a page actually turns out to need it —
            // and reused for every remaining page, then disposed at the end.
            TesseractEngine? ocrEngine = null;

            try
            {
                foreach (var page in document.GetPages())
                {
                    var text = page.Text;
                    if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= MinCharsPerPageForRealText)
                    {
                        pageTexts.Add(text);
                        continue;
                    }

                    ocrEngine ??= TryCreateOcrEngine();
                    pageTexts.Add(ocrEngine is null ? string.Empty : OcrPage(page, ocrEngine, filePath));
                }
            }
            finally
            {
                ocrEngine?.Dispose();
            }

            // Join per-page text with blank lines between pages as a
            // reasonable stand-in for paragraph boundaries — PDF text
            // extraction doesn't preserve true paragraph structure.
            return string.Join("\n\n", pageTexts);
        }
        catch (Exception ex)
        {
            // Covers corrupt files, password-protected/encrypted PDFs
            // (PdfPig throws when it can't open those), and any other
            // extraction failure. Logged, never thrown, per spec.
            _logger.LogError(ex, "Failed to extract text from PDF at {FilePath}", filePath);
            return string.Empty;
        }
    }

    private TesseractEngine? TryCreateOcrEngine()
    {
        var dataFile = Path.Combine(_tessDataPath, "eng.traineddata");
        if (!File.Exists(dataFile))
        {
            _logger.LogWarning(
                "OCR fallback skipped: {DataFile} was not found. This document has at least one " +
                "scanned/image-only page that will extract as empty text until that file is added.",
                dataFile);
            return null;
        }

        try
        {
            return new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the Tesseract OCR engine.");
            return null;
        }
    }

    private string OcrPage(UglyToad.PdfPig.Content.Page page, TesseractEngine engine, string filePath)
    {
        var textBuilder = new System.Text.StringBuilder();

        foreach (var image in page.GetImages())
        {
            byte[]? imageBytes;
            try
            {
                // TryGetPng handles most encodings PdfPig can convert (e.g.
                // CCITT fax-style scans) — but per PdfPig's own docs it does
                // NOT support JPEG. A plain scanned page saved as a JPEG
                // (the common case from most scanners/copiers) falls
                // through to RawMemory instead, which for JPEG images IS
                // the plain JPEG byte stream — something Tesseract's
                // underlying image library reads natively, no conversion
                // needed.
                imageBytes = image.TryGetPng(out var png) ? png : image.RawMemory.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR: could not read one embedded image in {FilePath}; skipping it.", filePath);
                continue;
            }

            if (imageBytes is null || imageBytes.Length == 0)
            {
                continue;
            }

            try
            {
                using var pix = Pix.LoadFromMemory(imageBytes);
                using TesseractPage ocrResult = engine.Process(pix);
                textBuilder.AppendLine(ocrResult.GetText());
            }
            catch (Exception ex)
            {
                // An unsupported/corrupt image encoding for this particular
                // embedded image — skip it and keep any text OCR'd from
                // other images or pages rather than failing the whole
                // document.
                _logger.LogWarning(ex, "OCR failed for one embedded image in {FilePath}; skipping it.", filePath);
            }
        }

        return textBuilder.ToString();
    }
}
