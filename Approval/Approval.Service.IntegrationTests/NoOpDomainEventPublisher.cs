using Approval.Service.Services;
using Messaging;

namespace Approval.Service.IntegrationTests;

/// <summary>
/// Test double for <see cref="IDomainEventPublisher"/> that records published
/// events in memory instead of contacting a Kafka broker. Integration tests run
/// without Kafka, so the production <c>KafkaDomainEventPublisher</c> (which
/// blocks on an unreachable broker) must be replaced by this implementation in
/// the test host.
/// </summary>
public sealed class NoOpDomainEventPublisher : IDomainEventPublisher
{
    private readonly List<PublishedEvent> published = new();

    public IReadOnlyList<PublishedEvent> Published => published;

    public Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default)
    {
        published.Add(new PublishedEvent(
            topic,
            message.EventId,
            message.EventType,
            message.Actor,
            message.Resource,
            message.Action,
            message.Outcome));

        return Task.CompletedTask;
    }

    public sealed record PublishedEvent(
        string Topic,
        Guid EventId,
        string EventType,
        string Actor,
        string? Resource,
        string Action,
        string Outcome);
}