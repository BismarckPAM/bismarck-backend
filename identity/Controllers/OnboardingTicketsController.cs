using System.Security.Claims;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Service.Controllers;

/// <summary>Public onboarding entry point — no authentication required.</summary>
[ApiController]
[Route("api/onboarding")]
public class OnboardingTicketsController(
    IOnboardingTicketService ticketService,
    ITurnstileVerifier turnstileVerifier) : ControllerBase
{
    /// <summary>
    /// Submit a registration request. Protected by Cloudflare Turnstile; a
    /// failed human verification yields 403 INVALID_CAPTCHA.
    /// </summary>
    [HttpPost("tickets")]
    [AllowAnonymous]
    public async Task<ActionResult<CreateOnboardingTicketResponse>> Create(
        CreateOnboardingTicketRequest request, CancellationToken cancellationToken)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var isHuman = await turnstileVerifier.VerifyAsync(request.TurnstileToken, remoteIp, cancellationToken);
        if (!isHuman)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "INVALID_CAPTCHA",
                message = "Human verification failed. Please complete the verification and try again."
            });
        }

        try
        {
            var response = await ticketService.CreateAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (DuplicateEmailException exception)
        {
            return Conflict(new { code = "DUPLICATE", message = exception.Message });
        }
    }
}

/// <summary>Administrator-only onboarding review queue.</summary>
[ApiController]
[Route("api/admin/onboarding")]
[Authorize(Roles = "Admin")]
public class AdminOnboardingTicketsController(IOnboardingTicketService ticketService) : ControllerBase
{
    [HttpGet("tickets")]
    public async Task<ActionResult<IReadOnlyList<OnboardingTicketResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await ticketService.GetAllAsync(cancellationToken));
    }

    [HttpPost("tickets/{id:guid}/approve")]
    public async Task<ActionResult<OnboardingTicketResponse>> Approve(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await ticketService.ApproveAsync(id, Reviewer(), cancellationToken));
    }

    [HttpPost("tickets/{id:guid}/reject")]
    public async Task<ActionResult<OnboardingTicketResponse>> Reject(
        Guid id, RejectOnboardingTicketRequest request, CancellationToken cancellationToken)
    {
        return Ok(await ticketService.RejectAsync(id, Reviewer(), request.Reason, cancellationToken));
    }

    private string Reviewer() =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.Identity?.Name
        ?? "unknown";
}