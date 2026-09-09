using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Service.Tests;

public class TokenServiceTests
{
    private const string Issuer = "Identity.Service.Tests";
    private const string Audience = "Bismarck.Services.Tests";
    private const string SigningKey = "unit-test-signing-key-that-is-at-least-32-characters";

    [Fact]
    public void CreateTokenReturnsTokenWithExpectedClaimsAndExpiry()
    {
        const int expirationMinutes = 15;
        var user = CreateUser();
        var before = DateTime.UtcNow.AddMinutes(expirationMinutes);

        var (token, expiresAt) = CreateService(expirationMinutes).CreateToken(user);
        var after = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.InRange(expiresAt, before.AddSeconds(-5), after.AddSeconds(5));
        Assert.Equal(user.Id.ToString(), decoded.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Id.ToString(), decoded.Claims.Single(claim => claim.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(user.Email, decoded.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(user.Email, decoded.Claims.Single(claim => claim.Type == ClaimTypes.Email).Value);
        Assert.Equal(user.Role.Name, decoded.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateTokenThrowsWhenSigningKeyIsMissingOrEmpty(string? signingKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:SigningKey"] = signingKey,
            ["JwtSettings:ExpirationMinutes"] = "15"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new TokenService(configuration).CreateToken(CreateUser()));
    }

    private static TokenService CreateService(int expirationMinutes)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:SigningKey"] = SigningKey,
            ["JwtSettings:ExpirationMinutes"] = expirationMinutes.ToString()
        }).Build());

    private static User CreateUser()
        => new()
        {
            Id = Guid.NewGuid(),
            Email = "person@example.com",
            Role = new Role { Name = "Admin" }
        };
}
