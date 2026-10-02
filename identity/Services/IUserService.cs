using Identity.Service.DTOs;

namespace Identity.Service.Services;

public interface IUserService
{
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin-only listing that includes DEACTIVATED accounts. The normal
    /// GetAllAsync deliberately hides them behind the User.IsActive global query
    /// filter, because existing consumers (Access Check) must only ever see
    /// usable accounts.
    /// </summary>
    Task<IReadOnlyList<UserResponse>> GetAllIncludingInactiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate/deactivate a single account. Idempotent: re-applying the current
    /// state returns the user unchanged rather than failing.
    /// </summary>
    /// <param name="actorUserId">The administrator performing the change, recorded on the audit event.</param>
    Task<UserResponse> UpdateStatusAsync(
        Guid id,
        UpdateUserStatusRequest request,
        string? actorUserId = null,
        CancellationToken cancellationToken = default);
}
