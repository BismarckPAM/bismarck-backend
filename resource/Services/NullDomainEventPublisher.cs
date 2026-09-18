namespace Resource.Service.Services;

public sealed class NullDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(DomainEventMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}