using System.Security.Claims;
using FluentValidation;
using Identity.Service.DTOs;
using Identity.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Service.Controllers;

[ApiController]
[Authorize]
[Route("api/identity/users")]
public class UsersController(IUserService userService, IValidator<CreateUserRequest> createValidator, IValidator<UpdateUserRequest> updateValidator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var response = await userService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
        => Ok(await userService.GetAllAsync(cancellationToken));

    /// <summary>
    /// Admin user directory — includes DEACTIVATED accounts.
    ///
    /// Deliberately a separate, role-restricted route rather than a flag on
    /// GET api/identity/users: the global `User.IsActive` query filter hides
    /// inactive rows from that endpoint (and therefore from Access Check), which
    /// would otherwise leave an Admin unable to find and reactivate an account.
    /// </summary>
    [Authorize(Roles = "Admin,Security Admin")]
    [HttpGet("admin/all")]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAllIncludingInactive(
        CancellationToken cancellationToken)
        => Ok(await userService.GetAllIncludingInactiveAsync(cancellationToken));

    /// <summary>
    /// Activate / deactivate a single account. Narrow on purpose: the Admin UI must
    /// not have to resend FullName, Email, RoleId and DepartmentId just to flip the
    /// status. Repeating the current status is a safe no-op.
    /// </summary>
    [Authorize(Roles = "Admin,Security Admin")]
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<UserResponse>> UpdateStatus(
        Guid id,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actor = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Ok(await userService.UpdateStatusAsync(id, request, actor, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await userService.GetByIdAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        return Ok(await userService.UpdateAsync(id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await userService.DeleteAsync(id, cancellationToken));
    }
}
