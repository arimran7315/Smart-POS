namespace SmartPOS.Infrastructure.Security;

public sealed class PasswordHasher
{
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < 12) throw new ArgumentException("Password must contain at least 12 characters.", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }
    public bool Verify(string password, string hash) => !string.IsNullOrWhiteSpace(password) && BCrypt.Net.BCrypt.Verify(password, hash);
}
