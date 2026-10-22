using SmartPOS.Infrastructure.Security;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify_NeverMatchPlaintextAndRejectWrongPassword()
    {
        var hasher = new PasswordHasher();
        const string password = "correct horse battery staple";
        var hash = hasher.Hash(password);
        Assert.NotEqual(password, hash);
        Assert.True(hasher.Verify(password, hash));
        Assert.False(hasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hash_RequiresAtLeastEightCharacters()
    {
        var hasher = new PasswordHasher();
        Assert.Throws<ArgumentException>(() => hasher.Hash("1234567"));
        Assert.NotEmpty(hasher.Hash("12345678"));
    }

    [Fact]
    public void HashAndVerify_SupportsLongUnicodePasswordsWithoutBcryptTruncation()
    {
        var hasher = new PasswordHasher();
        var prefix = string.Concat(Enumerable.Repeat("🙂", 30));
        var first = prefix + "x";
        var second = prefix + "y";

        var hash = hasher.Hash(first);

        Assert.True(hasher.Verify(first, hash));
        Assert.False(hasher.Verify(second, hash));
        Assert.False(hasher.NeedsRehash(hash));
    }

    [Fact]
    public void Verify_AcceptsLegacyBcryptAndMarksItForUpgrade()
    {
        var hasher = new PasswordHasher();
        var legacyHash = BCrypt.Net.BCrypt.HashPassword("legacy password", workFactor: 4);

        Assert.True(hasher.Verify("legacy password", legacyHash));
        Assert.True(hasher.NeedsRehash(legacyHash));
    }
}
