using Microsoft.AspNetCore.Mvc;
using MraPolicyChatbot.Data;
using MraPolicyChatbot.Services;

namespace MraPolicyChatbot.Controllers;

// Phase 3: basic username/password authentication with session-based login.
// No password reset, registration, email verification, 2FA, JWT, or
// external auth providers — see CLAUDE.md / docs/PROJECT_PLAN.md for scope.
public class AccountController : Controller
{
    private readonly UserRepository _userRepository;

    public AccountController(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? username, string? password)
    {
        // No password complexity validation — any non-empty credentials are
        // looked up as-is; the check below is what actually authenticates.
        var user = string.IsNullOrWhiteSpace(username)
            ? null
            : await _userRepository.GetByUsernameAsync(username);

        var isValid = user is not null
            && user.IsActive
            && !string.IsNullOrEmpty(password)
            && PasswordHasher.VerifyPassword(password, user.PasswordHash);

        if (!isValid)
        {
            ViewBag.ErrorMessage = "Invalid username or password";
            return View();
        }

        var authenticatedUser = user!;
        HttpContext.Session.SetUserId(authenticatedUser.Id);
        HttpContext.Session.SetUsername(authenticatedUser.Username);
        HttpContext.Session.SetRole(authenticatedUser.Role);

        return authenticatedUser.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Chat");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}
