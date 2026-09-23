using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Audit.Service.Services;

namespace Audit.Service.Controllers;

[Authorize]
[ApiController]
[Route("api/audit/logs")]
public class AuditController(IAuditService auditService) : ControllerBase
{
   
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetPending()
    {
        if (!auditService.IsApprover())
            return Forbid();

        return Ok(await auditService.GetPendingAsync());
    }