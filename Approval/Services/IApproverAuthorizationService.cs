using Approval.Service.DTOs;

namespace Approval.Service.Services;

public interface IApproverAuthorizationService
{
    Task<ApprovalRequestResponse> CreateAsync(CreateApprovalRequestRequest request);
    Task<IEnumerable<ApprovalRequestResponse>> GetPendingAsync();
    Task<IEnumerable<ApprovalRequestResponse>> GetAllAsync();
    Task<ApprovalRequestResponse> GetByIdAsync(Guid id);
    Task<ApprovalRequestResponse> UpdateAsync(Guid id, UpdateApprovalRequestRequest request);
    Task<ApprovalRequestResponse> DeleteAsync(Guid id);
    bool IsApprover();
}