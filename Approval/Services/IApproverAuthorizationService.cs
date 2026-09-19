using Approval.Service.DTOs;

namespace Approval.Service.Services;

public interface IApproverAuthorizationService
{
    Task<ApprovalRequestResponse> CreateAsync(CreateApprovalRequestRequest request);
    Task<IEnumerable<ApprovalRequestResponse>> GetPendingAsync();
    Task<ApprovalRequestResponse> ApproveAsync(Guid id);
    Task<ApprovalRequestResponse> RejectAsync(Guid id, string reason);
    Task<IEnumerable<ApprovalRequestResponse>> GetAllAsync();
    Task<ApprovalRequestResponse> GetByIdAsync(Guid id);
    Task<ApprovalRequestResponse> UpdateAsync(Guid id, UpdateApprovalRequestRequest request);
    Task<ApprovalRequestResponse> DeleteAsync(Guid id);
    bool IsApprover();
}