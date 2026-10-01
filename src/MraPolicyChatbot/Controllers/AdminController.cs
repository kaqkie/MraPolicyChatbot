using Microsoft.AspNetCore.Mvc;
using MraPolicyChatbot.Data;
using MraPolicyChatbot.Filters;
using MraPolicyChatbot.Models;
using MraPolicyChatbot.Services;

namespace MraPolicyChatbot.Controllers;

// Phase 4: real policy document management â€” list, upload, edit metadata
// and (optionally) the underlying file itself, and view the original
// uploaded file. No delete UI yet.
// Phase 5: kicks off text extraction/chunking after upload, after a file
// replacement via Edit Policy, and via the manual "Retry processing"
// button; shows processing status/chunk counts in the list.
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private static readonly string[] AllowedExtensions = { ".pdf", ".docx" };
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    private readonly PolicyRepository _policyRepository;
    private readonly PolicyChunkRepository _chunkRepository;
    private readonly AppSettingsRepository _settingsRepository;
    private readonly UserRepository _userRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminController> _logger;
    private readonly string _storageBasePath;

    public AdminController(
        PolicyRepository policyRepository,
        PolicyChunkRepository chunkRepository,
        AppSettingsRepository settingsRepository,
        UserRepository userRepository,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminController> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _policyRepository = policyRepository;
        _chunkRepository = chunkRepository;
        _settingsRepository = settingsRepository;
        _userRepository = userRepository;
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configuredPath = configuration["PolicyStorage:BasePath"] ?? "App_Data/PolicyFiles";
        _storageBasePath = Path.Combine(environment.ContentRootPath, configuredPath);
    }

    public async Task<IActionResult> Index()
    {
        var policies = await _policyRepository.GetAllAsync();

        var model = new List<PolicyListItemViewModel>();
        foreach (var p in policies)
        {
            model.Add(new PolicyListItemViewModel
            {
                Id = p.Id,
                Title = p.Title,
                Category = p.Category,
                UploadDate = DateOnly.FromDateTime(p.UploadDate),
                IsActive = p.IsActive,
                HasFile = !string.IsNullOrEmpty(p.FilePath),
                IsProcessed = p.IsProcessed,
                ProcessingError = p.ProcessingError,
                ChunkCount = await _chunkRepository.GetCountByPolicyIdAsync(p.Id),
            });
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Departments()
    {
        var departments = await _settingsRepository.GetDepartmentListAsync();
        var counts = await _policyRepository.GetCountsByCategoryAsync();

        ViewBag.Departments = departments;
        ViewBag.DepartmentCounts = counts;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDepartment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["SettingsErrorMessage"] = "Department name cannot be empty.";
            return RedirectToAction(nameof(Departments));
        }

        var list = await _settingsRepository.GetDepartmentListAsync();
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name.Trim());
            await _settingsRepository.SetDepartmentListAsync(list);
            TempData["SettingsSuccessMessage"] = "Department added.";
        }

        return RedirectToAction(nameof(Departments));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameDepartment(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            TempData["SettingsErrorMessage"] = "Invalid names.";
            return RedirectToAction(nameof(Departments));
        }

        var list = await _settingsRepository.GetDepartmentListAsync();
        var idx = list.FindIndex(d => string.Equals(d, oldName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            list[idx] = newName.Trim();
            await _settingsRepository.SetDepartmentListAsync(list);
            TempData["SettingsSuccessMessage"] = "Department renamed.";
        }

        return RedirectToAction(nameof(Departments));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDepartment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["SettingsErrorMessage"] = "Invalid name.";
            return RedirectToAction(nameof(Departments));
        }

        var list = await _settingsRepository.GetDepartmentListAsync();
        var removed = list.RemoveAll(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            await _settingsRepository.SetDepartmentListAsync(list);
            TempData["SettingsSuccessMessage"] = "Department deleted.";
        }

        return RedirectToAction(nameof(Departments));
    }

    [HttpGet]
    public async Task<IActionResult> UploadPolicy()
    {
        ViewBag.Departments = await _settingsRepository.GetDepartmentListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPolicy(string? title, string? category, IFormFile? file)
    {
        ViewBag.Departments = await _settingsRepository.GetDepartmentListAsync();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(category) || file is null || file.Length == 0)
        {
            ViewBag.ErrorMessage = "Title, Category, and a file are all required.";
            return View();
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            ViewBag.ErrorMessage = "Only PDF and DOCX files are supported.";
            return View();
        }

        if (file.Length > MaxFileSizeBytes)
        {
            ViewBag.ErrorMessage = "File exceeds the 20 MB size limit.";
            return View();
        }

        Directory.CreateDirectory(_storageBasePath);

        // Stored under a generated name to avoid collisions/path-traversal
        // issues with user-supplied file names. The human-readable name is
        // whatever the admin typed into Title, not the original filename.
        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var destinationPath = Path.Combine(_storageBasePath, storedFileName);

        await using (var stream = new FileStream(destinationPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var policy = new Policy
        {
            Title = title.Trim(),
            Category = category.Trim(),
            FilePath = storedFileName,
            UploadedBy = HttpContext.Session.GetUserId(),
            UploadDate = DateTime.UtcNow,
            Version = 1,
            IsActive = true,
        };

        var newPolicyId = await _policyRepository.CreateAsync(policy);

        // Phase 5: kick off extraction/chunking without blocking this
        // response. Uses its own DI scope rather than the request's scoped
        // services, since those get disposed once this response completes.
        TriggerProcessing(newPolicyId);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprocessPolicy(int id)
    {
        var policy = await _policyRepository.GetByIdAsync(id);
        if (policy is null)
        {
            return NotFound();
        }

        TriggerProcessing(id);

        TempData["PolicySuccessMessage"] = "Re-extracting text and rebuilding search embeddings for this file now. Refresh this page in a moment to see the result.";
        return RedirectToAction(nameof(Index));
    }

    private void TriggerProcessing(int policyId)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var processingService = scope.ServiceProvider.GetRequiredService<DocumentProcessingService>();

            try
            {
                await processingService.ProcessPolicyAsync(policyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background processing failed for policy {PolicyId}", policyId);
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditPolicy(int id)
    {
        var policy = await _policyRepository.GetByIdAsync(id);
        if (policy is null)
        {
            return NotFound();
        }

        ViewBag.Departments = await _settingsRepository.GetDepartmentListAsync();
        return View(new PolicyEditViewModel { Id = policy.Id, Title = policy.Title, Category = policy.Category });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPolicy(int id, string? title, string? category, IFormFile? replacementFile)
    {
        var policy = await _policyRepository.GetByIdAsync(id);
        if (policy is null)
        {
            return NotFound();
        }

        ViewBag.Departments = await _settingsRepository.GetDepartmentListAsync();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(category))
        {
            ViewBag.ErrorMessage = "Title and Category are required.";
            return View(new PolicyEditViewModel { Id = id, Title = title ?? string.Empty, Category = category ?? string.Empty });
        }

        string? oldFilePathToDelete = null;
        var fileReplaced = false;

        if (replacementFile is not null && replacementFile.Length > 0)
        {
            var extension = Path.GetExtension(replacementFile.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                ViewBag.ErrorMessage = "Only PDF and DOCX files are supported.";
                return View(new PolicyEditViewModel { Id = id, Title = title, Category = category });
            }

            if (replacementFile.Length > MaxFileSizeBytes)
            {
                ViewBag.ErrorMessage = "File exceeds the 20 MB size limit.";
                return View(new PolicyEditViewModel { Id = id, Title = title, Category = category });
            }

            Directory.CreateDirectory(_storageBasePath);
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var destinationPath = Path.Combine(_storageBasePath, storedFileName);

            await using (var stream = new FileStream(destinationPath, FileMode.Create))
            {
                await replacementFile.CopyToAsync(stream);
            }

            oldFilePathToDelete = policy.FilePath;
            policy.FilePath = storedFileName;
            policy.Version += 1;

            // Reset processing status immediately so the list doesn't show
            // a stale "Processed" (from the old file) while the new file is
            // being extracted/chunked in the background below.
            policy.IsProcessed = false;
            policy.ProcessingError = null;
            fileReplaced = true;
        }

        policy.Title = title.Trim();
        policy.Category = category.Trim();
        await _policyRepository.UpdateAsync(policy);

        if (fileReplaced)
        {
            // Best-effort cleanup of the file we just replaced â€” a failure
            // here (e.g. locked file) shouldn't block the save, since the
            // database now correctly points at the new one either way.
            if (!string.IsNullOrEmpty(oldFilePathToDelete))
            {
                try
                {
                    var oldPhysicalPath = Path.Combine(_storageBasePath, oldFilePathToDelete);
                    if (System.IO.File.Exists(oldPhysicalPath))
                    {
                        System.IO.File.Delete(oldPhysicalPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete replaced file for policy {PolicyId}", id);
                }
            }

            TriggerProcessing(id);
            TempData["PolicySuccessMessage"] = "Policy updated â€” the new file is being processed now. Refresh this page in a moment to see the chunk count.";
        }
        else
        {
            TempData["PolicySuccessMessage"] = "Policy updated.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ViewFile(int id)
    {
        var policy = await _policyRepository.GetByIdAsync(id);
        if (policy is null || string.IsNullOrEmpty(policy.FilePath))
        {
            return NotFound();
        }

        var physicalPath = Path.Combine(_storageBasePath, policy.FilePath);
        if (!System.IO.File.Exists(physicalPath))
        {
            // A bare 404 here used to leave the admin staring at the
            // browser's generic "not found" page with no idea why â€” send
            // them back to the list with an explanation and the fix
            // instead (same underlying situation ProcessPolicyAsync now
            // reports more clearly too).
            TempData["PolicyErrorMessage"] =
                $"\"{policy.Title}\"'s file can't be found in server storage anymore. Use Edit â†’ Replace file to re-upload it.";
            return RedirectToAction(nameof(Index));
        }

        var contentType = Path.GetExtension(policy.FilePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };

        return PhysicalFile(physicalPath, contentType, $"{policy.Title}{Path.GetExtension(policy.FilePath)}");
    }

    // Previously missing (see the class comment above, and the old
    // "No delete UI yet" note it carried) â€” there was no way to clear out
    // a policy that will never process successfully (e.g. its file was
    // lost from server storage and there's no copy to re-upload) short of
    // editing the database directly. Removes the chunks, the physical file
    // if one still exists, and the policy record itself.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePolicy(int id)
    {
        var policy = await _policyRepository.GetByIdAsync(id);
        if (policy is null)
        {
            return NotFound();
        }

        await _chunkRepository.DeleteByPolicyIdAsync(id);

        if (!string.IsNullOrEmpty(policy.FilePath))
        {
            try
            {
                var physicalPath = Path.Combine(_storageBasePath, policy.FilePath);
                if (System.IO.File.Exists(physicalPath))
                {
                    System.IO.File.Delete(physicalPath);
                }
            }
            catch (Exception ex)
            {
                // Best-effort â€” a locked/already-missing file shouldn't
                // block removing the record itself.
                _logger.LogWarning(ex, "Could not delete stored file for policy {PolicyId} during deletion.", id);
            }
        }

        await _policyRepository.DeleteAsync(id);

        TempData["PolicySuccessMessage"] = $"\"{policy.Title}\" was deleted.";
        return RedirectToAction(nameof(Index));
    }

    // Admin > Settings: AI connection details, the answer-matching
    // threshold, and the department list (dbo.AppSettings), plus a
    // separate "change my own password" form. Two POST actions rather than
    // one so a mistake filling in the password fields can't accidentally
    // fail/skip saving the other settings, and vice versa.
    [HttpGet]
    public async Task<IActionResult> Settings()
    {
        var model = new AdminSettingsViewModel
        {
            OllamaBaseUrl = await _settingsRepository.GetValueAsync(AppSettingsRepository.OllamaBaseUrlKey) ?? string.Empty,
            OllamaEmbeddingModel = await _settingsRepository.GetValueAsync(AppSettingsRepository.OllamaEmbeddingModelKey) ?? string.Empty,
            OllamaChatModel = await _settingsRepository.GetValueAsync(AppSettingsRepository.OllamaChatModelKey) ?? string.Empty,
            DepartmentList = string.Join('\n', await _settingsRepository.GetDepartmentListAsync()),
        };

        var thresholdRaw = await _settingsRepository.GetValueAsync(AppSettingsRepository.SimilarityThresholdKey);
        model.SimilarityThreshold = double.TryParse(thresholdRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var threshold)
            ? threshold
            : 0.5;

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeneralSettings(
        string? ollamaBaseUrl, string? ollamaEmbeddingModel, string? ollamaChatModel,
        string? similarityThreshold)
    {
        if (string.IsNullOrWhiteSpace(ollamaBaseUrl) ||
            string.IsNullOrWhiteSpace(ollamaEmbeddingModel) ||
            string.IsNullOrWhiteSpace(ollamaChatModel))
        {
            TempData["SettingsErrorMessage"] = "The Ollama address and both model names are required.";
            return RedirectToAction(nameof(Settings));
        }

        if (!Uri.TryCreate(ollamaBaseUrl.Trim(), UriKind.Absolute, out _))
        {
            TempData["SettingsErrorMessage"] = "The Ollama address needs to be a full URL, e.g. http://localhost:11434.";
            return RedirectToAction(nameof(Settings));
        }

        // Parsed with InvariantCulture explicitly (rather than relying on
        // default model binding for a double) so a "." decimal point from
        // the <input type="number"> always parses correctly regardless of
        // the server machine's regional settings.
        if (!double.TryParse(similarityThreshold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var threshold) ||
            threshold is < 0 or > 1)
        {
            TempData["SettingsErrorMessage"] = "The matching sensitivity must be a number between 0 and 1.";
            return RedirectToAction(nameof(Settings));
        }

        await _settingsRepository.SetValueAsync(AppSettingsRepository.OllamaBaseUrlKey, ollamaBaseUrl.Trim());
        await _settingsRepository.SetValueAsync(AppSettingsRepository.OllamaEmbeddingModelKey, ollamaEmbeddingModel.Trim());
        await _settingsRepository.SetValueAsync(AppSettingsRepository.OllamaChatModelKey, ollamaChatModel.Trim());
        await _settingsRepository.SetValueAsync(
            AppSettingsRepository.SimilarityThresholdKey,
            threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));

        TempData["SettingsSuccessMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string? currentPassword, string? newPassword, string? confirmNewPassword)
    {
        var userId = HttpContext.Session.GetUserId();
        var user = userId is null ? null : await _userRepository.GetByIdAsync(userId.Value);

        if (user is null)
        {
            // Session says logged in but the account is gone â€” shouldn't
            // normally happen; fail safely rather than guess.
            TempData["PasswordErrorMessage"] = "Could not find your account. Please log out and back in.";
            return RedirectToAction(nameof(Settings));
        }

        if (string.IsNullOrEmpty(currentPassword) || !PasswordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            TempData["PasswordErrorMessage"] = "Your current password is incorrect.";
            return RedirectToAction(nameof(Settings));
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["PasswordErrorMessage"] = "New password must be at least 6 characters.";
            return RedirectToAction(nameof(Settings));
        }

        if (newPassword != confirmNewPassword)
        {
            TempData["PasswordErrorMessage"] = "New password and confirmation don't match.";
            return RedirectToAction(nameof(Settings));
        }

        user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        await _userRepository.UpdateAsync(user);

        TempData["PasswordSuccessMessage"] = "Password changed.";
        return RedirectToAction(nameof(Settings));
    }
}
