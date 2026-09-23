namespace Messaging;

public static class KafkaTopics
{
    public const string AccessRequested = "access-requested";
    public const string AccessGranted = "access-granted";
    public const string AccessDenied = "access-denied";
    public const string ApprovalRequested = "approval-requested";
    public const string ApprovalGranted = "approval-granted";
    public const string ApprovalRejected = "approval-rejected";
    public const string PermissionRevoked = "permission-revoked";
}