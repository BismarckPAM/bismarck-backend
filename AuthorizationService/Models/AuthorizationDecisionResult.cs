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

public sealed record AuthorizationDecisionResult(
    AuthorizationDecision Decision,
    string Reason,
    DateTimeOffset? ExpiresAt = null,
    ApprovalRequirement ApprovalRequirement = ApprovalRequirement.NONE)
{
    public static AuthorizationDecisionResult Deny(string reason) =>
        new(AuthorizationDecision.DENY, reason);

    public static AuthorizationDecisionResult Allow(TimeSpan sessionDuration) =>
        new(AuthorizationDecision.ALLOW, "AUTHORIZED", DateTimeOffset.UtcNow.Add(sessionDuration));

    public static AuthorizationDecisionResult ApprovalRequired(string reason) =>
        new(
            AuthorizationDecision.APPROVAL_REQUIRED,
            reason,
            ApprovalRequirement: ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL);
}