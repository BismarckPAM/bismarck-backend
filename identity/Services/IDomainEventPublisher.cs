using Messaging;

namespace Identity.Service.Services; 

public interface IDomainEventPublisher
{
    Task PublishAsync<T>(string topic, SecurityEvent<T> message, CancellationToken cancellationToken = default);
}