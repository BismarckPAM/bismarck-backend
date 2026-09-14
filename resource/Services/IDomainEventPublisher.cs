namespace Resource.Service.Services;

public interface IDomainEventPublisher
{
    Task PublishAsync(DomainEventMessage message, CancellationToken cancellationToken = default);
}
