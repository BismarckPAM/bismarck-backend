using Messaging;

namespace Identity.Service.Services;

public sealed class NullDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
