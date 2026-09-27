namespace AuthorizationService.DTOs;

public sealed record JitSessionResponse(
    Guid Id,
    Guid ApprovalId,
    Guid UserId,
    string? UserEmail,
    Guid ResourceId,
    string? ResourceName,
    string? Action,
    int RequestedLevel,
    string Status,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid? RevokedByUserId,
    int RemainingSeconds,
    string? ProvisioningStatus,
    string? ProvisioningDetail);

public sealed record RevokeJitSessionRequest(string? Reason);