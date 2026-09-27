namespace Approval.Service.DTOs;

public sealed record ApprovalGrantedPayload(
    Guid ApprovalId,
    string RequesterUserId,
    string ResourceId,
    int RequestedLevel,
    int DurationMinutes,
    string? ReviewedByUserId,
    DateTime? ReviewedAt,
    // Enriched, human-readable context (best-effort) so downstream services can
    // provision/report without re-resolving raw UUIDs.
    string? RequesterName = null,
    string? RequesterEmail = null,
    string? ResourceName = null,
    string? ResourceType = null,
    string? Action = null
);
