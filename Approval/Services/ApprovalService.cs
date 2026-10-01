using System.Security.Claims;
using Approval.Service.Clients;
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
    IDomainEventPublisher domainEventPublisher,
    IIdentityContextClient? identityContextClient = null,
    IResourceContextClient? resourceContextClient = null) : IApproverAuthorizationService
{
    public async Task<ApprovalRequestResponse> CreateAsync(
        CreateApprovalRequestRequest request,
        CancellationToken cancellationToken = default)
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

        // Best-effort enrichment: store friendly display names now so every
        // later read (and Kafka event) is human-readable, never a raw UUID.
        await EnrichAsync(approvalRequest, cancellationToken);

        dbContext.ApprovalRequests.Add(approvalRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        await domainEventPublisher.PublishAsync(
            KafkaTopics.ApprovalRequested,
            new SecurityEvent<object>(
                Guid.NewGuid(),
                "ApprovalRequested",
                DateTimeOffset.UtcNow,
                requesterUserId,
                approvalRequest.ResourceId,
                "ELEVATED_ACCESS",
                "REQUESTED",
                new
                {
                    ApprovalId = approvalRequest.Id,
                    RequesterUserId = approvalRequest.RequesterUserId,
                    RequesterName = approvalRequest.RequesterName,
                    ResourceName = approvalRequest.ResourceName,
                    approvalRequest.RequestedLevel,
                    approvalRequest.DurationMinutes,
                    approvalRequest.Reason
                }),
            cancellationToken);

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
                .SetProperty(item => item.ReviewedByUserId, reviewerUserId)
                .SetProperty(item => item.UpdatedAt, reviewedAt),
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
            ReviewedAt: approvedRequest.ReviewedAt,
            RequesterName: approvedRequest.RequesterName,
            RequesterEmail: approvedRequest.RequesterEmail,
            ResourceName: approvedRequest.ResourceName,
            ResourceType: approvedRequest.ResourceType,
            Action: approvedRequest.Action,
            AzureVmName: approvedRequest.AzureVmName,
            AzureResourceGroup: approvedRequest.AzureResourceGroup,
            OsType: approvedRequest.OsType,
            PublicHost: approvedRequest.PublicHost
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

    public async Task<ApprovalRequestResponse> RejectAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var reviewerUserId = GetCurrentUserId();
        var reviewedAt = DateTime.UtcNow;
        var updatedRows = await dbContext.ApprovalRequests
            .Where(item => item.Id == id && item.Status == ApprovalStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, ApprovalStatus.REJECTED)
                .SetProperty(item => item.ReviewedAt, reviewedAt)
                .SetProperty(item => item.ReviewedByUserId, reviewerUserId)
                .SetProperty(item => item.RejectionReason, reason)
                .SetProperty(item => item.UpdatedAt, reviewedAt),
                cancellationToken);

        if (updatedRows == 0)
            throw await GetActionFailureAsync(id);

        var rejectedRequest = await GetByIdAsync(id);
        await domainEventPublisher.PublishAsync(
            KafkaTopics.ApprovalRejected,
            new SecurityEvent<object>(
                Guid.NewGuid(),
                "ApprovalRejected",
                DateTimeOffset.UtcNow,
                reviewerUserId,
                rejectedRequest.ResourceId,
                "ELEVATED_ACCESS",
                "REJECTED",
                new
                {
                    ApprovalId = rejectedRequest.Id,
                    RequesterUserId = rejectedRequest.RequesterUserId,
                    RejectionReason = rejectedRequest.RejectionReason,
                    ReviewedByUserId = rejectedRequest.ReviewedByUserId,
                    ReviewedAt = rejectedRequest.ReviewedAt
                }),
            cancellationToken);

        return rejectedRequest;
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

    public async Task<MyRequestsResponse> GetMyRequestsAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var requests = await dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(item => item.RequesterUserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = requests.Select(ToResponse).ToList();

        var counters = new RequestCounters(
            Total: items.Count,
            Pending: items.Count(item => item.Status == ApprovalStatus.PENDING),
            Approved: items.Count(item => item.Status == ApprovalStatus.APPROVED),
            Rejected: items.Count(item => item.Status == ApprovalStatus.REJECTED));

        return new MyRequestsResponse(items, counters);
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
        approvalRequest.UpdatedAt = DateTime.UtcNow;
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

    /// <summary>
    /// Best-effort identity/resource enrichment. Any failure degrades to a
    /// null label and never blocks submission (per the PAM contract).
    /// </summary>
    private async Task EnrichAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken)
    {
        approvalRequest.Action ??= "ELEVATED_ACCESS";

        if (identityContextClient is not null)
        {
            try
            {
                var identity = await identityContextClient.GetUserAsync(
                    approvalRequest.RequesterUserId, cancellationToken);
                if (identity is not null)
                {
                    approvalRequest.RequesterName = identity.Name;
                    approvalRequest.RequesterEmail = identity.Email;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Enrichment is advisory; ignore and continue.
            }
        }

        if (resourceContextClient is not null)
        {
            try
            {
                var resource = await resourceContextClient.GetResourceAsync(
                    approvalRequest.ResourceId, cancellationToken);
                if (resource is not null)
                {
                    approvalRequest.ResourceName = resource.DisplayName;
                    approvalRequest.ResourceType = resource.Type;
                    // Carry the VM targeting through so the JIT consumer does not
                    // have to call the Resource Service (it has no bearer token
                    // to do so from a background service).
                    approvalRequest.AzureVmName = resource.AzureVmName;
                    approvalRequest.AzureResourceGroup = resource.AzureResourceGroup;
                    approvalRequest.OsType = resource.OsType;
                    approvalRequest.PublicHost = resource.PublicHost;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Enrichment is advisory; ignore and continue.
            }
        }
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
            RequesterName = request.RequesterName,
            RequesterEmail = request.RequesterEmail,
            ResourceId = request.ResourceId,
            ResourceName = request.ResourceName,
            ResourceType = request.ResourceType,
            Action = request.Action,
            RequestedLevel = request.RequestedLevel,
            Reason = request.Reason,
            DurationMinutes = request.DurationMinutes,
            Status = request.Status,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt,
            ReviewedAt = request.ReviewedAt,
            ReviewedByUserId = request.ReviewedByUserId,
            RejectionReason = request.RejectionReason,
            AzureVmName = request.AzureVmName,
            AzureResourceGroup = request.AzureResourceGroup,
            OsType = request.OsType,
            PublicHost = request.PublicHost
        };
    }
}