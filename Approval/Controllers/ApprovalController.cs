using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Approval.Service.DTOs;
using Approval.Service.Services;

namespace Approval.Service.Controllers;

[Authorize]
[ApiController]
[Route("api/approval/requests")]
public class ApprovalController(IApproverAuthorizationService approverAuthorizationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApprovalRequestResponse>> Create(CreateApprovalRequestRequest request)
    {
        var approvalRequest = await approverAuthorizationService.CreateAsync(request);

        return CreatedAtAction(nameof(GetById), new { id = approvalRequest.Id }, approvalRequest);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApprovalRequestResponse>>> GetPending()
    {
        if (!approverAuthorizationService.IsApprover())
            return Forbid();

        return Ok(await approverAuthorizationService.GetPendingAsync());
    }

    [HttpPost("{id}/approve")]
    public async Task<ActionResult<ApprovalRequestResponse>> Approve(Guid id)
    {
        if (!approverAuthorizationService.IsApprover())
            return Forbid();

        try
        {
            return Ok(await approverAuthorizationService.ApproveAsync(id));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{id}/reject")]
    public async Task<ActionResult<ApprovalRequestResponse>> Reject(
        Guid id,
        RejectApprovalRequest request)
    {
        if (!approverAuthorizationService.IsApprover())
            return Forbid();

        try
        {
            return Ok(await approverAuthorizationService.RejectAsync(id, request.Reason));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApprovalRequestResponse>> GetById(Guid id)
    {
        var approvalRequest = await approverAuthorizationService.GetByIdAsync(id);

        return Ok(approvalRequest);
    }
}