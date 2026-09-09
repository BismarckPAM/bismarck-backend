namespace AuthorizationService.Models;

public sealed record UserDto(
    Guid Id,
    string Role,
    bool IsActive);

public sealed record ResourceDto(
    string Type,
    string Environment,
    string Criticality);