using System.ComponentModel.DataAnnotations;

namespace AuthorizationService.DTOs;

public sealed record UpsertPolicyRequest(
    [Required] string Role,
    string? ResourceType,
    [Required] string Environment,
    [Required] string Criticality,
    [Range(0, 5)] int MaxAccessLevel,
    bool RequiresApprovalForElevated);

public sealed record PolicyResponse(
    Guid Id,
    string Role,
    string ResourceType,
    string Environment,
    string Criticality,
    int MaxAccessLevel,
    bool RequiresApprovalForElevated,
    bool IsActive);
