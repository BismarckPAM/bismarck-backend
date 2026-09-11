namespace AuthorizationService.DTOs;

public sealed record AccessPolicyResponse(
    Guid Id,
    string Role,
    string ResourceType,
    string Environment,
    string Criticality,
    int MaxAccessLevel,
    bool RequiresApprovalForElevated,
    bool IsActive);