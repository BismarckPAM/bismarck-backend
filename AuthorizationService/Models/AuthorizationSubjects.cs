namespace AuthorizationService.Models;

public sealed record UserDto(
    Guid Id,
    string Role,
    bool IsActive);

public sealed record ResourceDto(
    Guid Id,
    string Type,
    string Environment,
    string Criticality);