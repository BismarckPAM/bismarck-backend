using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Controllers;

[ApiController]
[Route("authz/policies")]
public sealed class PoliciesController(AuthorizationDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PolicyResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var policies = await dbContext.AccessPolicies
            .AsNoTracking()
            .OrderBy(policy => policy.Role)
            .ThenBy(policy => policy.Environment)
            .ThenBy(policy => policy.Criticality)
            .ToListAsync(cancellationToken);

        return Ok(policies.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PolicyResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var policy = await dbContext.AccessPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(policy => policy.Id == id, cancellationToken);

        return policy is null ? NotFound(new { message = $"Policy '{id}' was not found." }) : Ok(ToResponse(policy));
    }

    [HttpPost]
    public async Task<ActionResult<PolicyResponse>> Create(
        UpsertPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        var duplicateExists = await MatchingPolicyQuery(normalized)
            .AnyAsync(cancellationToken);

        if (duplicateExists)
        {
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });
        }

        var policy = new AccessPolicy
        {
            Role = normalized.Role,
            Environment = normalized.Environment,
            Criticality = normalized.Criticality,
            MaxAccessLevel = normalized.MaxAccessLevel,
            RequiresApprovalForElevated = normalized.RequiresApprovalForElevated,
            IsActive = true
        };

        dbContext.AccessPolicies.Add(policy);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(policy);
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PolicyResponse>> Update(
        Guid id,
        UpsertPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.AccessPolicies
            .FirstOrDefaultAsync(policy => policy.Id == id, cancellationToken);

        if (policy is null)
        {
            return NotFound(new { message = $"Policy '{id}' was not found." });
        }

        var normalized = Normalize(request);
        var duplicateExists = await MatchingPolicyQuery(normalized)
            .AnyAsync(existing => existing.Id != id, cancellationToken);

        if (duplicateExists)
        {
            return BadRequest(new { message = "A policy with the same role, environment, and criticality already exists." });
        }

        policy.Role = normalized.Role;
        policy.Environment = normalized.Environment;
        policy.Criticality = normalized.Criticality;
        policy.MaxAccessLevel = normalized.MaxAccessLevel;
        policy.RequiresApprovalForElevated = normalized.RequiresApprovalForElevated;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(policy));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<PolicyResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var policy = await dbContext.AccessPolicies
            .FirstOrDefaultAsync(policy => policy.Id == id, cancellationToken);

        if (policy is null)
        {
            return NotFound(new { message = $"Policy '{id}' was not found." });
        }

        policy.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(policy));
    }

    private IQueryable<AccessPolicy> MatchingPolicyQuery(UpsertPolicyRequest policy) =>
        dbContext.AccessPolicies.Where(existing =>
            existing.Role == policy.Role &&
            existing.Environment == policy.Environment &&
            existing.Criticality == policy.Criticality);

    private static UpsertPolicyRequest Normalize(UpsertPolicyRequest request) =>
        request with
        {
            Role = NormalizeText(request.Role),
            ResourceType = NormalizeText(request.ResourceType ?? "VM"),
            Environment = NormalizeText(request.Environment),
            Criticality = NormalizeText(request.Criticality)
        };

    private static PolicyResponse ToResponse(AccessPolicy policy) =>
        new(
            policy.Id,
            policy.Role,
            "VM",
            policy.Environment,
            policy.Criticality,
            policy.MaxAccessLevel,
            policy.RequiresApprovalForElevated,
            policy.IsActive);

    private static string NormalizeText(string value) => value.Trim().ToUpperInvariant();
}
