namespace Messaging;

public sealed record SecurityEvent<TMetadata>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Actor,
    string? Resource,
    string Action,
    string Outcome,
    TMetadata Metadata
);