using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Controllers;

[ApiController]
[Route("authz/policies")]
public sealed class AccessPoliciesController(AuthorizationDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AccessPolicyResponse>> Create(
        AccessPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var values, out var validationError))
            return BadRequest(new { message = validationError });

        if (await PolicyExistsAsync(values, null, cancellationToken))
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });

        var policy = new AccessPolicy
        {
            Role = values.Role,
            ResourceType = values.ResourceType,
            Environment = values.Environment,
            Criticality = values.Criticality,
            MaxAccessLevel = values.MaxAccessLevel,
            RequiresApprovalForElevated = request.RequiresApprovalForElevated,
            IsActive = true
        };

        dbContext.AccessPolicies.Add(policy);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });
        }

        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, ToResponse(policy));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccessPolicyResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var policies = await dbContext.AccessPolicies
            .AsNoTracking()
            .OrderBy(policy => policy.Role)
            .ThenBy(policy => policy.ResourceType)
            .ThenBy(policy => policy.Environment)
            .ThenBy(policy => policy.Criticality)
            .ToListAsync(cancellationToken);

        return Ok(policies.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccessPolicyResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.AccessPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return policy is null
            ? NotFound(new { message = $"Policy with id '{id}' was not found." })
            : Ok(ToResponse(policy));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AccessPolicyResponse>> Update(
        Guid id,
        AccessPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var values, out var validationError))
            return BadRequest(new { message = validationError });

        var policy = await dbContext.AccessPolicies
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (policy is null)
            return NotFound(new { message = $"Policy with id '{id}' was not found." });

        if (await PolicyExistsAsync(values, id, cancellationToken))
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });

        policy.Role = values.Role;
        policy.ResourceType = values.ResourceType;
        policy.Environment = values.Environment;
        policy.Criticality = values.Criticality;
        policy.MaxAccessLevel = values.MaxAccessLevel;
        policy.RequiresApprovalForElevated = request.RequiresApprovalForElevated;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });
        }

        return Ok(ToResponse(policy));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<AccessPolicyResponse>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.AccessPolicies
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (policy is null)
            return NotFound(new { message = $"Policy with id '{id}' was not found." });

        policy.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(policy));
    }

    private async Task<bool> PolicyExistsAsync(
        NormalizedPolicy values,
        Guid? excludedId,
        CancellationToken cancellationToken) =>
        await dbContext.AccessPolicies.AnyAsync(policy =>
            policy.Role == values.Role
            && policy.ResourceType == values.ResourceType
            && policy.Environment == values.Environment
            && policy.Criticality == values.Criticality
            && (!excludedId.HasValue || policy.Id != excludedId.Value),
            cancellationToken);

    private static bool TryNormalize(
        AccessPolicyRequest request,
        out NormalizedPolicy values,
        out string? validationError)
    {
        if (request is null)
        {
            values = new NormalizedPolicy(string.Empty, string.Empty, string.Empty, string.Empty, 0);
            validationError = "Request body cannot be null.";
            return false;
        }

        values = new NormalizedPolicy(
            Normalize(request.Role),
            Normalize(request.ResourceType),
            Normalize(request.Environment),
            Normalize(request.Criticality),
            request.MaxAccessLevel);

        if (values.Role.Length is 0 or > 100)
        {
            validationError = "Role is required and must be at most 100 characters.";
            return false;
        }

        if (values.ResourceType.Length is 0 or > 100)
        {
            validationError = "ResourceType is required and must be at most 100 characters.";
            return false;
        }

        if (values.Environment.Length is 0 or > 50)
        {
            validationError = "Environment is required and must be at most 50 characters.";
            return false;
        }

        if (values.Criticality.Length is 0 or > 20)
        {
            validationError = "Criticality is required and must be at most 20 characters.";
            return false;
        }

        if (values.MaxAccessLevel is < 0 or > 5)
        {
            validationError = "MaxAccessLevel must be between 0 and 5.";
            return false;
        }

        validationError = null;
        return true;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static AccessPolicyResponse ToResponse(AccessPolicy policy) =>
        new(
            policy.Id,
            policy.Role,
            policy.ResourceType,
            policy.Environment,
            policy.Criticality,
            policy.MaxAccessLevel,
            policy.RequiresApprovalForElevated,
            policy.IsActive);

    private sealed record NormalizedPolicy(
        string Role,
        string ResourceType,
        string Environment,
        string Criticality,
        int MaxAccessLevel);
}