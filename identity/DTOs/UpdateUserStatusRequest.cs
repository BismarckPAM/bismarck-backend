namespace Identity.Service.DTOs;

/// <summary>
/// Body for the Admin account-status operation:
/// PATCH /api/identity/users/{id}/status
/// </summary>
/// <remarks>
/// Deliberately narrow. Flipping an account on/off must never require the
/// caller to re-send (and risk clobbering) FullName, Email, RoleId or
/// DepartmentId, which the full PUT /api/identity/users/{id} contract demands.
/// A dedicated request DTO needs no database migration.
/// </remarks>
public class UpdateUserStatusRequest
{
    /// <summary>Target account state: true = active (can sign in), false = deactivated.</summary>
    public bool IsActive { get; set; }
}