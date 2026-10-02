using Messaging;

namespace Analytics.Service.Models;

/// <summary>
/// The canonical BIS-402 metric definitions, expressed once so the API layer and
/// the verification documentation can never drift apart.
///
/// These mappings are deliberately NOT inferred from topic names alone. Note in
/// particular that the Authorization Service publishes its JIT manual-revocation
/// event to the <c>jit-revoked</c> topic but still stamps
/// <c>EventType = "PermissionRevoked"</c>, so revocation detection has to consider
/// both the event type and the source topic to cover every revocation flow.
/// </summary>
public static class AnalyticsMetricMap
{
    // EventType values exactly as emitted today by the Approval and
    // Authorization services (see ApprovalService.cs / AuthorizationController.cs).
    public const string ApprovalRequestedEventType = "ApprovalRequested";
    public const string ApprovalGrantedEventType = "ApprovalGranted";
    public const string ApprovalRejectedEventType = "ApprovalRejected";
    public const string AccessGrantedEventType = "AccessGranted";
    public const string AccessRequestedEventType = "AccessRequested";
    public const string AccessDeniedEventType = "AccessDenied";

    // BuildingBlocks/Messaging/SecurityEventTypes.PermissionRevoked
    public const string PermissionRevokedEventType = "PermissionRevoked";

    /// <summary>
    /// Bucket for a denial whose producer supplied no usable reason. Kept as a
    /// constant so "missing reason" always aggregates into one comparable row
    /// rather than splitting into nulls and blanks.
    /// </summary>
    public const string UnknownDenialReason = "UNKNOWN";

    /// <summary>
    /// REQUESTS - the canonical definition.
    /// Only ApprovalRequested counts. The user-facing "Request Access" page
    /// creates an Approval Request, which is what emits ApprovalRequested.
    /// AccessRequested is deliberately excluded: it is an authorization-engine
    /// decision stating "approval is required", so counting it would inflate the
    /// user-submitted request total.
    /// </summary>
    public static readonly IReadOnlyList<string> RequestEventTypes =
    [
        ApprovalRequestedEventType
    ];

    /// <summary>
    /// APPROVALS - the canonical definition.
    /// Only ApprovalGranted counts. AccessGranted is an authorization decision,
    /// not a human approval, so it must not be treated as one.
    /// </summary>
    public static readonly IReadOnlyList<string> ApprovalEventTypes =
    [
        ApprovalGrantedEventType
    ];

    /// <summary>
    /// DENIALS - the canonical definition.
    /// Both halves are counted so security analytics cover policy/authorization
    /// denials (AccessDenied) and human approval rejections (ApprovalRejected).
    /// </summary>
    public static readonly IReadOnlyList<string> DenialEventTypes =
    [
        AccessDeniedEventType,
        ApprovalRejectedEventType
    ];

    /// <summary>
    /// REVOCATIONS - the canonical definition.
    /// PermissionRevoked covers automatic JIT expiry and legacy/manual
    /// permission revocation. The Authorization Service's admin JIT manual
    /// revocation is published to the <c>jit-revoked</c> topic while still
    /// carrying the PermissionRevoked event type, so SourceTopicRevocationTopics
    /// is checked as well to make that flow explicit and self-documenting.
    /// Each stored row is counted exactly once (EventId is uniquely indexed).
    /// </summary>
    public static readonly IReadOnlyList<string> RevocationEventTypes =
    [
        PermissionRevokedEventType
    ];

    /// <summary>Kafka topics that represent a revocation, regardless of event type.</summary>
    public static readonly IReadOnlyList<string> SourceTopicRevocationTopics =
    [
        KafkaTopics.PermissionRevoked,
        KafkaTopics.JitRevoked
    ];

    /// <summary>
    /// Every event type that contributes to at least one of the four headline
    /// metrics. Queries filter to this set before grouping, which guarantees the
    /// summary totals are exactly the sum of the trend buckets and keeps
    /// activity such as logins or resource changes out of the numbers.
    /// </summary>
    public static IReadOnlyList<string> AllMetricEventTypes =>
    [
        .. RequestEventTypes,
        .. ApprovalEventTypes,
        .. DenialEventTypes,
        .. RevocationEventTypes
    ];

    /// <summary>
    /// Metadata key holding the authorization decision reason.
    /// Emitted by AuthorizationService as <c>Reason</c>.
    /// </summary>
    public const string ReasonMetadataKey = "Reason";

    /// <summary>
    /// Metadata key holding the approver's rejection reason.
    /// Emitted by ApprovalService as <c>RejectionReason</c>.
    /// </summary>
    public const string RejectionReasonMetadataKey = "RejectionReason";

    /// <summary>
    /// Metadata key holding the friendly resource label. Producers serialize
    /// with camelCase, so the lower-cased form is accepted too (JsonElement
    /// property lookup is case-sensitive).
    /// </summary>
    public const string ResourceNameMetadataKey = "ResourceName";
    public const string ResourceNameMetadataKeyCamel = "resourceName";
}
