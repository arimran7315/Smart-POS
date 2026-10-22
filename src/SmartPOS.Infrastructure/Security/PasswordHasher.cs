using System.Security.Cryptography;
using System.Text;

namespace SmartPOS.Infrastructure.Security;

public sealed class PasswordHasher
{
    public const int MinimumPasswordLength = 8;
    private const string CurrentHashPrefix = "$smartpos-sha256-bcrypt$";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinimumPasswordLength) throw new ArgumentException($"Password must contain at least {MinimumPasswordLength} characters.", nameof(password));
        // BCrypt only considers the first 72 UTF-8 bytes. Pre-hashing avoids silent
        // truncation and preserves support for long and non-ASCII passphrases.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        return CurrentHashPrefix + BCrypt.Net.BCrypt.HashPassword(digest, workFactor: 12);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash)) return false;
        if (hash.StartsWith(CurrentHashPrefix, StringComparison.Ordinal))
        {
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
            return BCrypt.Net.BCrypt.Verify(digest, hash[CurrentHashPrefix.Length..]);
        }

        // Existing installations contain raw BCrypt hashes. Continue to verify those;
        // successful authentication upgrades the hash in AuthenticationService.
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    public bool NeedsRehash(string hash) => !hash.StartsWith(CurrentHashPrefix, StringComparison.Ordinal);
}
