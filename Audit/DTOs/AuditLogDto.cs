namespace Audit.Service.DTOs;

// Query parameters from the URL
public record AuditLogQueryParameters(
    string? User = null,
    string? Resource = null,
    string? EventType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20
);

// Standard paginated wrapper response
public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}