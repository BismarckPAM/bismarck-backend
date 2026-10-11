using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AuthorizationService.DTOs;

public sealed record AuthorizationCheckRequest(
    [property: JsonRequired]
    Guid UserId,
    [property: JsonRequired]
    Guid ResourceId,
    string Action,
    [param: Range(1, 1440)]
    [property: JsonRequired]
    int SessionDurationMinutes = 120);