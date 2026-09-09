using Identity.Service.Services;
using Xunit;

namespace Identity.Service.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashingPasswordThenVerifyingCorrectPasswordReturnsTrue()
    {
        var hasher = new PasswordHasher();
        var password = "CorrectPassword123!";

        var hash = hasher.Hash(password);

        Assert.True(hasher.Verify(password, hash));
    }

    [Fact]
    public void VerifyingIncorrectPasswordReturnsFalse()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("CorrectPassword123!");

        Assert.False(hasher.Verify("WrongPassword123!", hash));
    }

    [Fact]
    public void HashingSamePasswordTwiceProducesDifferentValidHashes()
    {
        var hasher = new PasswordHasher();
        var password = "CorrectPassword123!";

        var firstHash = hasher.Hash(password);
        var secondHash = hasher.Hash(password);

        Assert.NotEqual(firstHash, secondHash);
        Assert.True(hasher.Verify(password, firstHash));
        Assert.True(hasher.Verify(password, secondHash));
    }
}
