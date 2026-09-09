namespace AuthorizationService.Models;

public enum AuthorizationDecision
{
    DENY,
    ALLOW,
    APPROVAL_REQUIRED
}

public enum ApprovalRequirement
{
    NONE,
    MANUAL_OR_AUTOMATED_DUAL_APPROVAL
}

public enum AuthorizationDenialReason
{
    USER_NOT_FOUND,
    USER_DEACTIVATED,
    USER_ROLE_NOT_ASSIGNED,
    RESOURCE_NOT_FOUND,
    UNKNOWN_ACTION,
    INSUFFICIENT_ROLE_PERMISSIONS,
    OUTSIDE_MAINTENANCE_WINDOW,
    UPSTREAM_SERVICE_UNAVAILABLE,
    SYSTEM_ERROR_FAIL_CLOSED
}

public sealed record AuthorizationDecisionResult(
    AuthorizationDecision Decision,
    string Reason,
    DateTimeOffset? ExpiresAt = null,
    ApprovalRequirement ApprovalRequirement = ApprovalRequirement.NONE,
    string? Details = null)
{
    public static AuthorizationDecisionResult Deny(
        AuthorizationDenialReason reason,
        string? details = null) =>
        new(
            AuthorizationDecision.DENY,
            reason.ToString(),
            Details: details ?? GetDefaultDetails(reason));

    public static AuthorizationDecisionResult Allow(TimeSpan sessionDuration) =>
        new(AuthorizationDecision.ALLOW, "AUTHORIZED", DateTimeOffset.UtcNow.Add(sessionDuration));

    public static AuthorizationDecisionResult ApprovalRequired(string reason) =>
        new(
            AuthorizationDecision.APPROVAL_REQUIRED,
            reason,
            ApprovalRequirement: ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL);

    private static string GetDefaultDetails(AuthorizationDenialReason reason) => reason switch
    {
        AuthorizationDenialReason.USER_NOT_FOUND => "The requested user could not be verified.",
        AuthorizationDenialReason.USER_DEACTIVATED => "The user account is inactive.",
        AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED => "The user does not have an assigned role.",
        AuthorizationDenialReason.RESOURCE_NOT_FOUND => "The requested resource could not be verified.",
        AuthorizationDenialReason.UNKNOWN_ACTION => "The requested action is not supported.",
        AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS => "The assigned role does not permit this action.",
        AuthorizationDenialReason.OUTSIDE_MAINTENANCE_WINDOW => "The action is not permitted outside the maintenance window.",
        AuthorizationDenialReason.UPSTREAM_SERVICE_UNAVAILABLE => "A required authorization dependency is temporarily unavailable.",
        AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED => "The authorization system could not safely complete this check.",
        _ => "Authorization was denied."
    };
}