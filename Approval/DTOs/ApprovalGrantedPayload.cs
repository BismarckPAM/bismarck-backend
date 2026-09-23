namespace Approval.Service.DTOs;

public sealed record ApprovalGrantedPayload(
    string RequesterUserId,
    string ResourceId,
    string RequestedLevel,
    int DurationMinutes,
    string? ReviewedByUserId,
    DateTime? ReviewedAt
);