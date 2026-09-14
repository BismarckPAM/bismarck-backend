namespace Resource.Service.Services;

public sealed record DomainEventMessage(
    string EventType,
    Guid EntityId,
    DateTimeOffset Timestamp,
    object Payload);
