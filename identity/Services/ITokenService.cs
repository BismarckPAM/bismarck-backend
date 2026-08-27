using Identity.Service.Models;

namespace Identity.Service.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}
