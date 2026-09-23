using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MraPolicyChatbot.Services;

namespace MraPolicyChatbot.Filters;

// Phase 3: hand-rolled session-based authorization filter. Deliberately NOT
// using Microsoft.AspNetCore.Authorization / cookie authentication — this
// is a simple, explicit check against session state, matching the "basic
// username/password + session" scope of this phase.
//
// Usage:
//   [Authorize]                    -> must be logged in (any role)
//   [Authorize(Roles = "Admin")]   -> must be logged in AND have that role
public class AuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public string? Roles { get; set; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var session = context.HttpContext.Session;

        if (!session.IsLoggedIn())
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        if (!string.IsNullOrWhiteSpace(Roles))
        {
            var allowedRoles = Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var currentRole = session.GetRole();

            if (currentRole is null || !allowedRoles.Contains(currentRole, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            }
        }
    }
}
