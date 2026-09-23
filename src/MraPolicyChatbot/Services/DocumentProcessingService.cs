using System.Text.Json;
using MraPolicyChatbot.Data;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Services;

// Phase 5: orchestrates extract -> chunk -> store for one policy.
// Phase 6: also generates an embedding per chunk (via Ollama) so the
// chatbot can retrieve relevant chunks later. Embedding generation degrades
// gracefully — if Ollama is unavailable, chunks are still extracted,
// chunked, and stored with a null Embedding rather than failing the whole
// policy; they're simply skipped by chat retrieval until re-processed.
public class DocumentProcessingService
{
    private readonly PolicyRepository _policyRepository;
    private readonly PolicyChunkRepository _chunkRepository;
    private readonly PdfTextExtractor _pdfExtractor;
    private readonly DocxTextExtractor _docxExtractor;
    private readonly OllamaClient _ollamaClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        PolicyRepository policyRepository,
        PolicyChunkRepository chunkRepository,
        PdfTextExtractor pdfExtractor,
        DocxTextExtractor docxExtractor,
        OllamaClient ollamaClient,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DocumentProcessingService> logger)
    {
        _policyRepository = policyRepository;
        _chunkRepository = chunkRepository;
        _pdfExtractor = pdfExtractor;
        _docxExtractor = docxExtractor;
        _ollamaClient = ollamaClient;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task ProcessPolicyAsync(int policyId)
    {
        var policy = await _policyRepository.GetByIdAsync(policyId);
        if (policy is null || string.IsNullOrEmpty(policy.FilePath))
        {
            _logger.LogWarning("ProcessPolicyAsync: policy {PolicyId} not found or has no stored file.", policyId);
            return;
        }

        var configuredPath = _configuration["PolicyStorage:BasePath"] ?? "App_Data/PolicyFiles";
        var basePath = Path.Combine(_environment.ContentRootPath, configuredPath);
        var physicalPath = Path.Combine(basePath, policy.FilePath);
        var extension = Path.GetExtension(policy.FilePath).ToLowerInvariant();

        // Tracked separately from the generic "couldn't extract text" error
        // below: a missing physical file is a completely different problem
        // (nothing to even try extracting from) and was previously reported
        // to the admin with the same misleading "may be a scanned image
        // without OCR, corrupt, or password-protected" message — which
        // sent them looking for an OCR/corruption problem that didn't
        // exist. The usual cause is the app having been launched from a
        // different working directory than when the file was uploaded
        // (ContentRootPath, and therefore where App_Data/PolicyFiles
        // resolves to, differs between "dotnet run" from the project
        // folder vs. running the built .exe/.dll directly from bin/) —
        // consistently launching the app the same way going forward avoids
        // this.
        string text;
        string? missingFileError = null;
        if (!File.Exists(physicalPath))
        {
            _logger.LogError("ProcessPolicyAsync: file not found for policy {PolicyId} at {Path}", policyId, physicalPath);
            text = string.Empty;
            missingFileError =
                "The uploaded file for this policy can't be found in server storage anymore — it may have been " +
                "moved or deleted outside the app, or the app was started from a different folder than when it " +
                "was uploaded. This isn't an OCR or file-corruption problem; there's simply nothing on disk to " +
                "read. Fix it via Edit → Replace file to re-upload the document.";
        }
        else
        {
            text = extension switch
            {
                ".pdf" => _pdfExtractor.ExtractText(physicalPath),
                ".docx" => _docxExtractor.ExtractText(physicalPath),
                _ => string.Empty,
            };
        }

        var chunks = TextChunker.ChunkText(text);

        // Re-processing support: clear whatever chunks exist from a
        // previous run before inserting the new set.
        await _chunkRepository.DeleteByPolicyIdAsync(policyId);

        var order = 0;
        var embeddingFailures = 0;
        foreach (var chunkText in chunks)
        {
            var embedding = await _ollamaClient.GetEmbeddingAsync(chunkText);
            string? embeddingJson = null;
            if (embedding is not null)
            {
                embeddingJson = JsonSerializer.Serialize(embedding);
            }
            else
            {
                embeddingFailures++;
            }

            await _chunkRepository.CreateAsync(new PolicyChunk
            {
                PolicyId = policyId,
                ChunkText = chunkText,
                ChunkOrder = order++,
                Embedding = embeddingJson,
                CreatedDate = DateTime.UtcNow,
            });
        }

        if (embeddingFailures > 0 && chunks.Count > 0)
        {
            _logger.LogWarning(
                "ProcessPolicyAsync: {FailureCount} of {TotalCount} chunks for policy {PolicyId} were stored without an embedding (Ollama unreachable?). They will be skipped by chat retrieval until re-processed.",
                embeddingFailures, chunks.Count, policyId);
        }

        var success = chunks.Count > 0;
        var error = success
            ? null
            : missingFileError ?? "No text could be extracted from this file. It may be a scanned image without OCR, corrupt, or password-protected.";

        if (!success)
        {
            _logger.LogWarning("ProcessPolicyAsync: no text extracted for policy {PolicyId} ({FilePath}).", policyId, policy.FilePath);
        }

        await _policyRepository.UpdateProcessingResultAsync(policyId, success, text, error);
    }
}
