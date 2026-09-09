namespace AuthorizationService.Models;

public enum AuthorizationDecision
{
    DENY,
    ALLOW,
    APPROVAL_REQUIRED
}

public sealed record AuthorizationDecisionResult(
    AuthorizationDecision Decision,
    string Reason,
    DateTimeOffset? ExpiresAt = null)
{
    public static AuthorizationDecisionResult Deny(string reason) =>
        new(AuthorizationDecision.DENY, reason);

    public static AuthorizationDecisionResult Allow(TimeSpan sessionDuration) =>
        new(AuthorizationDecision.ALLOW, "AUTHORIZED", DateTimeOffset.UtcNow.Add(sessionDuration));

    public static AuthorizationDecisionResult ApprovalRequired(string reason) =>
        new(AuthorizationDecision.APPROVAL_REQUIRED, reason);
}