namespace SmartPOS.Infrastructure.Security;

public sealed class PasswordHasher
{
    public const int MinimumPasswordLength = 12;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength) throw new ArgumentException($"Password must contain at least {MinimumPasswordLength} characters.", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }
    public bool Verify(string password, string hash) => !string.IsNullOrWhiteSpace(password) && BCrypt.Net.BCrypt.Verify(password, hash);
}
