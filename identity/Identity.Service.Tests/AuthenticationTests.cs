using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Service.Tests;

public class AuthenticationTests
{
    [Fact]
    public void PasswordHasherHashesAndVerifiesWithoutStoringPlaintext()
    {
        var hasher = new PasswordHasher();
        var password = "CorrectPassword123!";
        var hash = hasher.Hash(password);

        Assert.NotEqual(password, hash);
        Assert.True(hasher.Verify(password, hash));
        Assert.False(hasher.Verify("WrongPassword123!", hash));
    }

    [Fact]
    public void TokenServiceIncludesUserClaims()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "Identity.Service",
            ["JwtSettings:Audience"] = "Bismarck.Services",
            ["JwtSettings:SigningKey"] = "unit-test-signing-key-that-is-at-least-32-characters",
            ["JwtSettings:ExpirationMinutes"] = "60"
        }).Build();
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "person@example.com",
            Role = new Role { Name = "Admin" }
        };

        var (token, expiresAt) = new TokenService(configuration).CreateToken(user);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

        Assert.Equal(user.Id.ToString(), claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Admin", claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.True(expiresAt > DateTime.UtcNow);
    }
}