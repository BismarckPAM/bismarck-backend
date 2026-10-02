namespace Analytics.Service.Models;

/// <summary>
/// Normalized, immutable record of a single consumed SecurityEvent.
///
/// The Analytics Service owns its own database, so this row is an independent
/// copy of the event that Audit also stores. Because every row is preserved
/// (rather than a mutable counter), aggregates can always be recalculated and
/// reconciled against the audit trail — which is what BIS-402's accuracy
/// requirement depends on.
/// </summary>
public sealed class AnalyticsEvent
{
    public Guid Id { get; set; }

    /// <summary>
    /// The SecurityEvent.EventId. Unique-indexed: this is the Kafka idempotency
    /// guard, so redelivery of the same offset can never double-count.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>Kafka topic the event was read from, retained for auditability.</summary>
    public string SourceTopic { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// UTC calendar day of <see cref="OccurredAt"/>. Persisted rather than computed
    /// with a database-specific date_trunc at query time so that the daily trend
    /// grouping is an indexable equality predicate that behaves identically on
    /// every EF Core provider (PostgreSQL in production, in-memory in unit tests).
    /// </summary>
    public DateOnly OccurredAtDate { get; set; }

    public string Actor { get; set; } = string.Empty;

    /// <summary>Raw SecurityEvent.Resource identifier, when present.</summary>
    public string? Resource { get; set; }

    /// <summary>
    /// Human-readable resource label resolved at ingestion: Metadata.ResourceName
    /// when the producer supplied one, otherwise the Resource identifier.
    /// </summary>
    public string? ResourceName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// Normalized denial reason for AccessDenied / ApprovalRejected events
    /// (Metadata.Reason and Metadata.RejectionReason respectively).
    /// Null for every non-denial event type.
    /// </summary>
    public string? DenialReason { get; set; }

    /// <summary>Raw metadata JSON, preserved verbatim (stored as jsonb).</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset ConsumedAt { get; set; }
}
