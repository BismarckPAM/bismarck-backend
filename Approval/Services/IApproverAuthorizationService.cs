using Approval.Service.DTOs;

namespace Approval.Service.Services;

public interface IApproverAuthorizationService
{
    Task<ApprovalRequestResponse> CreateAsync(
        CreateApprovalRequestRequest request,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<ApprovalRequestResponse>> GetPendingAsync();

    /// <summary>
    /// The authenticated user's own request history plus aggregate counters,
    /// served from the database so it survives browser sessions.
    /// </summary>
    Task<MyRequestsResponse> GetMyRequestsAsync(CancellationToken cancellationToken = default);

    Task<ApprovalRequestResponse> ApproveAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<ApprovalRequestResponse> RejectAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<ApprovalRequestResponse>> GetAllAsync();
    Task<ApprovalRequestResponse> GetByIdAsync(Guid id);
    Task<ApprovalRequestResponse> UpdateAsync(Guid id, UpdateApprovalRequestRequest request);
    Task<ApprovalRequestResponse> DeleteAsync(Guid id);
    bool IsApprover();
}