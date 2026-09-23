using Messaging;

namespace AuthorizationService.Services;

public interface IAuthorizationEventPublisher
{
    Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default);
}