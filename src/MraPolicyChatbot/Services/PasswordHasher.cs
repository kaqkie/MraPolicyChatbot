namespace MraPolicyChatbot.Services;

// Phase 3: thin wrapper around BCrypt.Net-Next. No password complexity
// rules, no reset/rotation logic — just hash and verify.
public static class PasswordHasher
{
    private const int WorkFactor = 10;

    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);
    }

    public static bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
