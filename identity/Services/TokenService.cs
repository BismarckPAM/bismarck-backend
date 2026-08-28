using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.Service.Models;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Service.Services;

public class TokenService(IConfiguration configuration) : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var settings = configuration.GetSection("JwtSettings");
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.GetValue<int>("ExpirationMinutes", 60));
        var signingKey = settings["SigningKey"]
            ?? throw new InvalidOperationException("JwtSettings:SigningKey is not configured.");
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("JwtSettings:SigningKey is not configured.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.Name)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings["Issuer"],
            audience: settings["Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
