using Analytics.Service.DTOs;
using Analytics.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Analytics.Service.Controllers;

/// <summary>
/// BIS-402 read API. Every endpoint requires a valid Identity Service JWT
/// (<c>[Authorize]</c>) - the analytics figures are security-sensitive and must
/// never be public. Role modelling is intentionally left to BIS-405; this story
/// only requires that a caller be authenticated.
/// </summary>
[Authorize]
[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
public class AnalyticsController(IAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>Default cap for the ranked lists, protecting against unbounded payloads.</summary>
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    /// <summary>
    /// AC-2: request / approval / denial / revocation totals for the period,
    /// plus the daily UTC trend.
    /// GET /api/analytics/summary?startDate=&amp;endDate=
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AnalyticsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AnalyticsSummaryResponse>> GetSummary(
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        CancellationToken cancellationToken)
    {
        var parsed = AnalyticsDateRangeParser.Parse(startDate, endDate);
        var invalid = ValidateRange(parsed);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await analyticsService.GetSummaryAsync(parsed.Range, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// AC-3: most-requested resources, ranked. Derived from ApprovalRequested
    /// events only.
    /// GET /api/analytics/top-resources?startDate=&amp;endDate=&amp;limit=
    /// </summary>
    [HttpGet("top-resources")]
    [ProducesResponseType(typeof(TopResourcesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TopResourcesResponse>> GetTopResources(
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var parsed = AnalyticsDateRangeParser.Parse(startDate, endDate);
        var invalid = ValidateRange(parsed);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await analyticsService.GetTopResourcesAsync(
            parsed.Range,
            NormalizeLimit(limit),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// AC-4: denial-reason distribution across AccessDenied and ApprovalRejected.
    /// GET /api/analytics/denial-reasons?startDate=&amp;endDate=&amp;limit=
    /// </summary>
    [HttpGet("denial-reasons")]
    [ProducesResponseType(typeof(DenialReasonsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DenialReasonsResponse>> GetDenialReasons(
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var parsed = AnalyticsDateRangeParser.Parse(startDate, endDate);
        var invalid = ValidateRange(parsed);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await analyticsService.GetDenialReasonsAsync(
            parsed.Range,
            NormalizeLimit(limit),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Builds a 400 response naming the offending query parameter. Returns null
    /// when the range is valid, so each action reads as
    /// "validate, otherwise query".
    /// </summary>
    private ActionResult? ValidateRange(AnalyticsDateRangeParseResult parsed) =>
        parsed.IsValid
            ? null
            : BadRequest(new ValidationProblemDetails(parsed.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid analytics date range"
            });

    /// <summary>
    /// Clamps the optional page size into a sane range so a caller cannot request
    /// an unbounded payload.
    /// </summary>
    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or < 1)
        {
            return DefaultLimit;
        }

        return limit > MaxLimit ? MaxLimit : limit.Value;
    }
}
