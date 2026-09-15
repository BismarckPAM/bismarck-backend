using FluentValidation;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Services;
using Identity.Service.Validators;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Identity.Service.Controllers;

[ApiController]
[Route("api/identity/auth")]
public class AuthController(
    IdentityDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IValidator<LoginRequest> loginValidator) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .Include(item => item.Role)
            .Include(item => item.Department)
            .SingleOrDefaultAsync(item => item.Email == email, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        var (token, expiresAt) = tokenService.CreateToken(user);
        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = new LoginUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.Name,
                Department = user.Department.Name
            }
        });
    }
}
