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
    string? Action = null,
    // Azure VM targeting resolved at approval time. Carried in the event because
    // the JIT consumer runs as a background service with no HTTP context, so it
    // cannot call the Resource Service itself - that endpoint requires a bearer
    // token. Enriching here, where the caller's token exists, follows the same
    // "enrich at write time" rule this payload already uses.
    string? AzureVmName = null,
    string? AzureResourceGroup = null,
    string? OsType = null,
    string? PublicHost = null
);
