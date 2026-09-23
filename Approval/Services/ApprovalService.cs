using System.Security.Claims;
using Approval.Service.Data;
using Approval.Service.DTOs;
using Approval.Service.Models;
using Microsoft.EntityFrameworkCore;
using Messaging;

namespace Approval.Service.Services;

public sealed class ApprovalService(
	ApprovalDbContext dbContext,
	IHttpContextAccessor httpContextAccessor,
	IConfiguration configuration,
	IDomainEventPublisher domainEventPublisher) : IApproverAuthorizationService
{
	public async Task<ApprovalRequestResponse> CreateAsync(
		CreateApprovalRequestRequest request)
	{
		var requesterUserId = GetCurrentUserId();
		var approvalRequest = new ApprovalRequest
		{
			RequesterUserId = requesterUserId,
			ResourceId = request.ResourceId,
			RequestedLevel = request.RequestedLevel,
			Reason = request.Reason,
			DurationMinutes = request.DurationMinutes,
			Status = ApprovalStatus.PENDING,
			CreatedAt = DateTime.UtcNow
		};

		dbContext.ApprovalRequests.Add(approvalRequest);
		await dbContext.SaveChangesAsync();

		return ToResponse(approvalRequest);
	}

    public async Task<ApprovalRequestResponse> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reviewerUserId = GetCurrentUserId();
        var reviewedAt = DateTime.UtcNow;
    
        // Pass cancellationToken to EF Core
        var updatedRows = await dbContext.ApprovalRequests
            .Where(item => item.Id == id && item.Status == ApprovalStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, ApprovalStatus.APPROVED)
                .SetProperty(item => item.ReviewedAt, reviewedAt)
                .SetProperty(item => item.ReviewedByUserId, reviewerUserId),
                cancellationToken);
    
        if (updatedRows == 0)
            throw await GetActionFailureAsync(id);
    
        var approvedRequest = await GetByIdAsync(id);
    
        var payload = new ApprovalGrantedPayload(
            ApprovalId: approvedRequest.Id,
            RequesterUserId: approvedRequest.RequesterUserId,
            ResourceId: approvedRequest.ResourceId,
            RequestedLevel: approvedRequest.RequestedLevel,
            DurationMinutes: approvedRequest.DurationMinutes,
            ReviewedByUserId: approvedRequest.ReviewedByUserId,
            ReviewedAt: approvedRequest.ReviewedAt
        );
    
        // Pass cancellationToken to Kafka publisher
        await domainEventPublisher.PublishAsync(
			KafkaTopics.ApprovalGranted,
			new SecurityEvent<ApprovalGrantedPayload>(
				Guid.NewGuid(),
                "ApprovalGranted",
                DateTimeOffset.UtcNow,
				approvedRequest.ReviewedByUserId ?? reviewerUserId,
				approvedRequest.ResourceId,
				"ELEVATED_ACCESS",
				"APPROVED",
                payload
            ),
            cancellationToken
        );
    
        return approvedRequest;
    }

	public async Task<ApprovalRequestResponse> RejectAsync(Guid id, string reason)
	{
		var reviewerUserId = GetCurrentUserId();
		var reviewedAt = DateTime.UtcNow;
		var updatedRows = await dbContext.ApprovalRequests
			.Where(item => item.Id == id && item.Status == ApprovalStatus.PENDING)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(item => item.Status, ApprovalStatus.REJECTED)
				.SetProperty(item => item.ReviewedAt, reviewedAt)
				.SetProperty(item => item.ReviewedByUserId, reviewerUserId)
				.SetProperty(item => item.RejectionReason, reason));

		if (updatedRows == 0)
			throw await GetActionFailureAsync(id);

		return await GetByIdAsync(id);
	}

	public async Task<IEnumerable<ApprovalRequestResponse>> GetPendingAsync()
	{
		var requests = await dbContext.ApprovalRequests
			.AsNoTracking()
			.Where(item => item.Status == ApprovalStatus.PENDING)
			.OrderBy(item => item.CreatedAt)
			.ToListAsync();

		return requests.Select(ToResponse);
	}

	public async Task<IEnumerable<ApprovalRequestResponse>> GetAllAsync()
	{
		var requests = await dbContext.ApprovalRequests
			.AsNoTracking()
			.OrderByDescending(item => item.CreatedAt)
			.ToListAsync();

		return requests.Select(ToResponse);
	}

	public async Task<ApprovalRequestResponse> GetByIdAsync(Guid id)
	{
		var approvalRequest = await dbContext.ApprovalRequests
			.AsNoTracking()
			.SingleOrDefaultAsync(item => item.Id == id)
			?? throw new KeyNotFoundException($"Approval request {id} was not found.");

		return ToResponse(approvalRequest);
	}

	public async Task<ApprovalRequestResponse> UpdateAsync(
		Guid id,
		UpdateApprovalRequestRequest request)
	{
		var approvalRequest = await dbContext.ApprovalRequests
			.SingleOrDefaultAsync(item => item.Id == id)
			?? throw new KeyNotFoundException($"Approval request {id} was not found.");

		approvalRequest.RequestedLevel = request.RequestedLevel;
		approvalRequest.Reason = request.Reason;
		approvalRequest.DurationMinutes = request.DurationMinutes;
		await dbContext.SaveChangesAsync();

		return ToResponse(approvalRequest);
	}

	public async Task<ApprovalRequestResponse> DeleteAsync(Guid id)
	{
		var approvalRequest = await dbContext.ApprovalRequests
			.SingleOrDefaultAsync(item => item.Id == id)
			?? throw new KeyNotFoundException($"Approval request {id} was not found.");

		dbContext.ApprovalRequests.Remove(approvalRequest);
		await dbContext.SaveChangesAsync();

		return ToResponse(approvalRequest);
	}

	public bool IsApprover()
	{
		var approverRoles = configuration.GetSection("Approval:ApproverRoles")
			.Get<string[]>() ?? [];
		var user = httpContextAccessor.HttpContext?.User;

		return user is not null && approverRoles.Any(user.IsInRole);
	}

	private string GetCurrentUserId()
	{
		return httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
			?? throw new UnauthorizedAccessException("The authenticated user ID is missing.");
	}

	private async Task<Exception> GetActionFailureAsync(Guid id)
	{
		var status = await dbContext.ApprovalRequests
			.Where(item => item.Id == id)
			.Select(item => (ApprovalStatus?)item.Status)
			.SingleOrDefaultAsync();

		return status is null
			? new KeyNotFoundException($"Approval request {id} was not found.")
			: new InvalidOperationException("The approval request has already been actioned.");
	}

	private static ApprovalRequestResponse ToResponse(ApprovalRequest request)
	{
		return new ApprovalRequestResponse
		{
			Id = request.Id,
			RequesterUserId = request.RequesterUserId,
			ResourceId = request.ResourceId,
			RequestedLevel = request.RequestedLevel,
			Reason = request.Reason,
			DurationMinutes = request.DurationMinutes,
			Status = request.Status,
			CreatedAt = request.CreatedAt,
			ReviewedAt = request.ReviewedAt,
			ReviewedByUserId = request.ReviewedByUserId,
			RejectionReason = request.RejectionReason
		};
	}
}
