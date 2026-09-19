using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Approval.Service.DTOs;
using Approval.Service.Exceptions;
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

    [HttpGet("{id}")]
    public async Task<ActionResult<ApprovalRequestResponse>> GetById(Guid id)
    {
        var approvalRequest = await approverAuthorizationService.GetByIdAsync(id);

        return Ok(approvalRequest);
    }
}

[Route("api/approval/requests/{id}/approve")]

[Route("api/approval/requests/{id}/reject")]