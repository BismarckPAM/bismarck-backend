namespace Approval.Service.Services;

public sealed record DomainEventMessage<T>(
    string EventType,
    Guid EntityId,
    DateTimeOffset Timestamp,
    T Payload);
