namespace Approval.Service.Services;

public interface IDomainEventPublisher
{
    Task PublishAsync<T>(DomainEventMessage<T> message, CancellationToken cancellationToken = default);
}