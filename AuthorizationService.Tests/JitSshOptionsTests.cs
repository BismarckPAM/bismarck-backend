using AuthorizationService.Services;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// The broker used to hardcode a single shared login ("azureuser"), so a JIT
/// session for user@gmail.com could only ever connect as azureuser even though
/// the UI showed `ssh user@gmail.com@vm`. These tests pin the corrected
/// resolution: the session's own account wins, and a key is found for it.
/// </summary>
public sealed class JitSshOptionsTests
{
    private const string Pem = "-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n-----END OPENSSH PRIVATE KEY-----";

    [Fact]
    public void ResolveLogin_PrefersTheSessionsOwnEmail()
    {
        var options = new JitSshOptions { Username = "azureuser" };

        Assert.Equal("user@gmail.com", options.ResolveLogin("user@gmail.com"));
    }

    [Fact]
    public void ResolveLogin_FallsBackToSharedUsernameWhenNoEmail()
    {
        var options = new JitSshOptions { Username = "azureuser" };

        Assert.Equal("azureuser", options.ResolveLogin(null));
        Assert.Equal("azureuser", options.ResolveLogin("   "));
    }

    [Fact]
    public void ResolveLogin_AppliesTemplateToTheEmail()
    {
        var options = new JitSshOptions { UsernameTemplate = "{email}@pam" };

        Assert.Equal("user@gmail.com@pam", options.ResolveLogin("user@gmail.com"));
    }

    [Fact]
    public void ResolveLogin_TemplateWithoutEmailIsUnusable()
    {
        var options = new JitSshOptions { Username = "azureuser", UsernameTemplate = "{email}@pam" };

        // A template cannot be filled in, so falling back to the shared account
        // would silently log in as the wrong user.
        Assert.Null(options.ResolveLogin(null));
    }

    [Fact]
    public void TryResolveKey_PrefersTheKeyRegisteredForThatUser()
    {
        var options = new JitSshOptions
        {
            PrivateKey = "-----BEGIN SHARED-----\nshared\n-----END SHARED-----",
            PrivateKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user@gmail.com"] = Pem
            }
        };

        var resolved = options.TryResolveKey("user@gmail.com", out var pem, out var path);

        Assert.True(resolved);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", pem);
        Assert.Null(path);
    }

    [Fact]
    public void TryResolveKey_FallsBackToTheSharedKeyForAnUnmappedUser()
    {
        var options = new JitSshOptions { PrivateKey = Pem };

        var resolved = options.TryResolveKey("someone.else@gmail.com", out var pem, out var path);

        Assert.True(resolved);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", pem);
        Assert.Null(path);
    }

    /// <summary>
    /// A PEM that arrived through an environment variable has its newlines as the
    /// literal two-character sequence "\n". Left unescaped, the key is silently
    /// malformed and authentication fails with a baffling parse error.
    /// </summary>
    [Fact]
    public void TryResolveKey_RepairsEscapedNewlinesInInlinePem()
    {
        var escaped = "-----BEGIN OPENSSH PRIVATE KEY-----\\nline1\\nline2\\n-----END OPENSSH PRIVATE KEY-----";
        var options = new JitSshOptions { PrivateKey = escaped };

        options.TryResolveKey("user@gmail.com", out var pem, out _);

        Assert.NotNull(pem);
        Assert.DoesNotContain("\\n", pem);
        Assert.Contains("\n", pem);
    }

    [Fact]
    public void TryResolveKey_ResolvesAFileNamedAfterTheLoginFromTheKeyDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("jit-keys");
        try
        {
            var keyPath = Path.Combine(directory.FullName, "user@gmail.com");
            File.WriteAllText(keyPath, Pem);

            var options = new JitSshOptions { PrivateKeyDirectory = directory.FullName };

            var resolved = options.TryResolveKey("user@gmail.com", out var pem, out var path);

            Assert.True(resolved);
            Assert.Null(pem);
            Assert.Equal(keyPath, path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The login arrives from the JIT session, which is influenced by user
    /// supplied profile data. A traversal attempt must not reach a key outside
    /// the configured directory.
    /// </summary>
    [Fact]
    public void TryResolveKey_RejectsTraversalOutOfTheKeyDirectory()
    {
        var root = Directory.CreateTempSubdirectory("jit-keys-root");
        try
        {
            var keyDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "keys"));
            // The stolen key lives OUTSIDE the directory the broker may read from.
            var outsideDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "outside"));
            File.WriteAllText(Path.Combine(outsideDirectory.FullName, "id_rsa"), Pem);

            var options = new JitSshOptions { PrivateKeyDirectory = keyDirectory.FullName };

            var resolved = options.TryResolveKey("../outside/id_rsa", out var pem, out var path);

            Assert.False(resolved);
            Assert.Null(pem);
            Assert.Null(path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsConfigured_TrueWhenOnlyPerUserKeysExist()
    {
        var options = new JitSshOptions
        {
            PrivateKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user@gmail.com"] = Pem
            }
        };

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_FalseWithNoCredentialsAtAll()
    {
        Assert.False(new JitSshOptions().IsConfigured);
    }

    [Theory]
    [InlineData("SHA256:abc123", "abc123")]
    [InlineData("abc123=", "abc123")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void NormalizeFingerprint_StripsPrefixAndPadding(string? configured, string? expected)
    {
        Assert.Equal(expected, JitSshOptions.NormalizeFingerprint(configured));
    }
}