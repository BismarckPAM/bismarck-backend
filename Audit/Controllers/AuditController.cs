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
    // GET /api/audit/logs?search=&user=&resource=&eventType=&outcome=&from=&to=&page=&pageSize=
    //
    //   search   - free-text, partial, case-insensitive across Actor OR Resource
    //   user     - exact Actor match
    //   resource - exact Resource match
    //   eventType- exact EventType match
    //   outcome  - exact Outcome match
    //   from / to - inclusive OccurredAt bounds
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLog>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<AuditLog>>> GetLogs(
        [FromQuery] AuditLogQueryParameters query, 
        CancellationToken cancellationToken)
    {
        // An inverted range is a caller mistake, not something to silently repair:
        // swapping the bounds would return events the caller did not ask for. A
        // clear 400 is returned instead. (Malformed dates are already rejected by
        // model binding before this point.)
        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["from"] =
                [
                    $"'from' ({query.From.Value:O}) must be less than or equal to 'to' ({query.To.Value:O})."
                ]
            })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid audit date range"
            });
        }

        var result = await auditService.GetLogsAsync(query, cancellationToken);
        return Ok(result);
    }

    // No create POST, PUT, PATCH, or DELETE
    // Audit logs must remain strictly immutable
}