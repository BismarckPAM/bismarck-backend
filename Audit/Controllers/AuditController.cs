using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Audit.Service.DTOs;
using Audit.Service.Models;
using Audit.Service.Services;

namespace Audit.Service.Controllers;

[Authorize]
[ApiController]
[Route("api/audit/logs")]
public class AuditController(IAuditService auditService) : ControllerBase
{
    // GET /api/audit/logs?user=admin&eventType=security.auth.login&page=1&pageSize=10
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLog>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLog>>> GetLogs(
        [FromQuery] AuditLogQueryParameters query, 
        CancellationToken cancellationToken)
    {
        var result = await auditService.GetLogsAsync(query, cancellationToken);
        return Ok(result);
    }

    // No create POST, PUT, PATCH, or DELETE
    // Audit logs must remain strictly immutable
}