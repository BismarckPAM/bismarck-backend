using Audit.Service.DTOs;
using Audit.Service.Models;

namespace Audit.Service.Services;

public interface IAuditService
{
    Task<PagedResult<AuditLog>> GetLogsAsync(AuditLogQueryParameters query, CancellationToken cancellationToken = default);
}