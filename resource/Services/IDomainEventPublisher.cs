using Messaging;

namespace Resource.Service.Services;

public interface IDomainEventPublisher
{
    Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default);
}
