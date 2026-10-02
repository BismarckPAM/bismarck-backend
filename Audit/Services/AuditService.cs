using Microsoft.EntityFrameworkCore;
using Audit.Service.Data;
using Audit.Service.DTOs;
using Audit.Service.Models;

namespace Audit.Service.Services;

public class AuditService(AuditDbContext dbContext) : IAuditService
{
    // LIKE/ILIKE escape character used for the free-text Search filter.
    private const string LikeEscapeCharacter = "\\";

    public async Task<PagedResult<AuditLog>> GetLogsAsync(AuditLogQueryParameters query, CancellationToken cancellationToken = default)
    {
        // 1. Start with base read-only query
        var dbQuery = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        // 2. Free-text Search (BIS-403): partial, case-insensitive match across
        //    Actor OR Resource. This is a genuine SQL predicate - EF Core translates
        //    EF.Functions.ILike into PostgreSQL `ILIKE`, so the table is never pulled
        //    into memory. The pattern is passed as a *parameter*, never concatenated
        //    into SQL, so a hostile search value cannot alter the statement.
        //    Explicit user/resource filters are applied separately further down and
        //    compose with this one using AND semantics.
        var search = Normalize(query.Search);
        if (search is not null)
        {
            var pattern = BuildContainsPattern(search);
            dbQuery = dbQuery.Where(l =>
                EF.Functions.ILike(l.Actor, pattern, LikeEscapeCharacter)
                || (l.Resource != null && EF.Functions.ILike(l.Resource, pattern, LikeEscapeCharacter)));
        }

        // 3. Filter by 'user' (Actor) - exact match, unchanged existing semantics
        var user = Normalize(query.User);
        if (user is not null)
        {
            dbQuery = dbQuery.Where(l => l.Actor == user);
        }

        // 4. Filter by 'resource' - exact match, unchanged existing semantics
        var resource = Normalize(query.Resource);
        if (resource is not null)
        {
            dbQuery = dbQuery.Where(l => l.Resource == resource);
        }

        // 5. Filter by 'eventType'
        var eventType = Normalize(query.EventType);
        if (eventType is not null)
        {
            dbQuery = dbQuery.Where(l => l.EventType == eventType);
        }

        // 6. Filter by 'outcome' (BIS-403)
        var outcome = Normalize(query.Outcome);
        if (outcome is not null)
        {
            dbQuery = dbQuery.Where(l => l.Outcome == outcome);
        }

        // 7. Filter date range ('from' and 'to')
        if (query.From.HasValue)
        {
            dbQuery = dbQuery.Where(l => l.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            dbQuery = dbQuery.Where(l => l.OccurredAt <= query.To.Value);
        }

        // 8. Get total count matching criteria before paging
        int totalCount = await dbQuery.CountAsync(cancellationToken);

        // Sanitize page and pageSize (cap at 100 max)
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        // 9. Paginate and sort by most recent first.
        //    Id is the tie-breaker (BIS-403): several events can share an exact
        //    OccurredAt, and without a unique secondary key PostgreSQL is free to
        //    return them in any order, which makes Skip/Take duplicate or skip rows
        //    across pages. Id is the primary key, so the ordering is total and the
        //    pagination is stable.
        var items = await dbQuery
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Trims a filter value and treats a blank result as "filter not supplied",
    /// so <c>?user=%20</c> behaves like an absent filter instead of matching a
    /// literal space.
    /// </summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Wraps the search term in wildcards and escapes the characters that would
    /// otherwise act as wildcards, so searching for <c>100%</c> or <c>a_b</c>
    /// looks for that literal text instead of matching everything.
    /// </summary>
    internal static string BuildContainsPattern(string search)
    {
        var escaped = search
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}