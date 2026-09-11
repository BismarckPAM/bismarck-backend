using AuthorizationService.Data;
using AuthorizationService.Models;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Services;

public sealed class PolicyDecisionEngine(AuthorizationDbContext dbContext)
    : IPolicyDecisionEngine
{
    private static readonly IReadOnlyDictionary<string, int> ActionLevelMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["VIEW_LOGS"] = 1,
            ["READ_STATUS"] = 1,
            ["EXECUTE_QUERY"] = 2,
            ["APP_CONNECT"] = 2,
            ["SSH_ACCESS"] = 3,
            ["DEPLOY_BUILD"] = 3,
            ["DB_MIGRATION"] = 4,
            ["CONFIG_WRITE"] = 4,
            ["ADMIN_WRITE"] = 4,
            ["SCHEMA_UPDATE"] = 4,
            ["ROOT_ACCESS"] = 5,
            ["ROOT"] = 5,
            ["DROP_TABLE"] = 5
        };

    public async Task<AuthorizationDecisionResult> EvaluateAsync(
        UserDto user,
        ResourceDto resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsActive)
            return AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_DEACTIVATED);

        if (string.IsNullOrWhiteSpace(user.Role))
            return AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);

        if (string.IsNullOrWhiteSpace(action)
            || !ActionLevelMap.TryGetValue(action, out var requiredLevel))
            return AuthorizationDecisionResult.Deny(AuthorizationDenialReason.UNKNOWN_ACTION);

        AccessPolicy? policy;
        try
        {
            policy = await dbContext.AccessPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.IsActive
                    && item.Role.ToUpper() == user.Role.ToUpper()
                    && (item.ResourceType == string.Empty
                        || item.ResourceType.ToUpper() == resource.Type.ToUpper())
                    && item.Environment.ToUpper() == resource.Environment.ToUpper()
                    && item.Criticality.ToUpper() == resource.Criticality.ToUpper(),
                    cancellationToken);
        }
        catch (DbException)
        {
            return AuthorizationDecisionResult.Deny(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED);
        }

        var maxAllowedLevel = policy?.MaxAccessLevel ?? 0;
        if (requiredLevel > maxAllowedLevel)
            return AuthorizationDecisionResult.Deny(AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);

        var isProduction = resource.Environment.Equals("PROD", StringComparison.OrdinalIgnoreCase)
            || resource.Environment.Equals("PRODUCTION", StringComparison.OrdinalIgnoreCase);
        var isCritical = resource.Criticality.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase);

        if ((isProduction || isCritical)
            && requiredLevel >= 4)
        {
            return AuthorizationDecisionResult.ApprovalRequired(
                "ELEVATED_PRIVILEGE_ON_CRITICAL_RESOURCE");
        }

        if (isProduction
            && user.Role.Equals("Developer", StringComparison.OrdinalIgnoreCase)
            && requiredLevel >= 3)
        {
            return AuthorizationDecisionResult.ApprovalRequired(
                "PRODUCTION_ACCESS_REQUIRES_APPROVAL");
        }

        return AuthorizationDecisionResult.Allow(TimeSpan.FromHours(2));
    }
}