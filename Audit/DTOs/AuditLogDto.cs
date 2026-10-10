namespace Audit.Service.DTOs;

// Query parameters from the URL
//
// BIS-403 extended this contract with two additional filters, APPENDED to the
// end of the positional parameter list on purpose: every pre-existing call site
// (positional or named) keeps compiling unchanged, so existing consumers of
// GET /api/audit/logs are not broken by the Audit Log viewer work.
//
//   Search  - free-text, partial, case-insensitive match across Actor OR
//             Resource. Distinct from User/Resource, which stay exact-match.
//   Outcome - exact match on the recorded outcome (SUCCESS / DENIED / ...).
public record AuditLogQueryParameters(
    string? User = null,
    string? Resource = null,
    string? EventType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Outcome = null
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