using System.ComponentModel.DataAnnotations;

namespace AuthorizationService.DTOs;

public sealed record AuthorizationCheckRequest(
    Guid UserId,
    Guid ResourceId,
    string Action,
    [param: Range(1, 1440)] int SessionDurationMinutes = 120);