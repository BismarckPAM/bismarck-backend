using Microsoft.EntityFrameworkCore;
using Audit.Service.Data;
using Audit.Service.DTOs;
using Audit.Service.Models;

namespace Audit.Service.Services;

public class AuditService(AuditDbContext dbContext) : IAuditService
{
    public async Task<PagedResult<AuditLog>> GetLogsAsync(AuditLogQueryParameters query, CancellationToken cancellationToken = default)
    {
        // 1. Start with base read-only query
        var dbQuery = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        // 2. Filter by 'user' (Actor)
        if (!string.IsNullOrWhiteSpace(query.User))
        {
            dbQuery = dbQuery.Where(l => l.Actor == query.User);
        }

        // 3. Filter by 'resource'
        if (!string.IsNullOrWhiteSpace(query.Resource))
        {
            dbQuery = dbQuery.Where(l => l.Resource == query.Resource);
        }

        // 4. Filter by 'eventType'
        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            dbQuery = dbQuery.Where(l => l.EventType == query.EventType);
        }

        // 5. Filter date range ('from' and 'to')
        if (query.From.HasValue)
        {
            dbQuery = dbQuery.Where(l => l.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            dbQuery = dbQuery.Where(l => l.OccurredAt <= query.To.Value);
        }

        // 6. Get total count matching criteria before paging
        int totalCount = await dbQuery.CountAsync(cancellationToken);

        // Sanitize page and pageSize (cap at 100 max)
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        // 7. Paginate and sort by most recent first
        var items = await dbQuery
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, totalCount, page, pageSize);
    }
}