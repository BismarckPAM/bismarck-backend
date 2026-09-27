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

    // Domain events published by the Identity and Resource services.
    // These were previously published but never consumed (BUG-001).
    public const string IdentityEvents = "identity-events";
    public const string ResourceEvents = "resource-events";

    // Every topic that is published to the bus. Consumers that record a full
    // history (Audit) subscribe to this set so that no event is dropped.
    public static readonly string[] All =
    [
        AccessRequested,
        AccessGranted,
        AccessDenied,
        ApprovalRequested,
        ApprovalGranted,
        ApprovalRejected,
        PermissionRevoked,
        IdentityEvents,
        ResourceEvents
    ];
}