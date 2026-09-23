namespace MraPolicyChatbot.Services;

// Phase 3: small helpers around ASP.NET Core's built-in ISession so
// controllers/filters don't need to know the session key names or do their
// own type conversions. No business logic here — just plumbing.
public static class SessionExtensions
{
    private const string UserIdKey = "UserId";
    private const string UsernameKey = "Username";
    private const string RoleKey = "Role";

    public static void SetUserId(this ISession session, int userId) =>
        session.SetInt32(UserIdKey, userId);

    public static int? GetUserId(this ISession session) =>
        session.GetInt32(UserIdKey);

    public static void SetUsername(this ISession session, string username) =>
        session.SetString(UsernameKey, username);

    public static string? GetUsername(this ISession session) =>
        session.GetString(UsernameKey);

    public static void SetRole(this ISession session, string role) =>
        session.SetString(RoleKey, role);

    public static string? GetRole(this ISession session) =>
        session.GetString(RoleKey);

    public static bool IsLoggedIn(this ISession session) =>
        session.GetUserId() is not null;
}
