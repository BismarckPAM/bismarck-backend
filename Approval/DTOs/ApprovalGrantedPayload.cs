namespace Approval.Service.DTOs;

public sealed record ApprovalGrantedPayload(
    Guid ApprovalId,
    string RequesterUserId,
    string ResourceId,
    int RequestedLevel,
    int DurationMinutes,
    string? ReviewedByUserId,
    DateTime? ReviewedAt
);