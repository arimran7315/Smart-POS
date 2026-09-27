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
    public void Hash_RequiresTwelveCharacters()
    {
        Assert.Throws<ArgumentException>(() => new PasswordHasher().Hash("short"));
    }
}
