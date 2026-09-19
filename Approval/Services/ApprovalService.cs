using System.Security.Claims;
using Approval.Service.Data;
using Approval.Service.DTOs;
using Approval.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Approval.Service.Services;

public sealed class ApprovalService(
	ApprovalDbContext dbContext,
	IHttpContextAccessor httpContextAccessor) : IApproverAuthorizationService
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

	private string GetCurrentUserId()
	{
		return httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
			?? throw new UnauthorizedAccessException("The authenticated user ID is missing.");
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
