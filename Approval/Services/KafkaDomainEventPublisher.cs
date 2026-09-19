using System.Text.Json;

namespace Approval.Service.Services;

public sealed class KafkaDomainEventPublisher(IConfiguration configuration)
    : IDomainEventPublisher
{
    private readonly string topic = configuration["Kafka:Topic"]
        ?? "bismarck.domain-events";

    public Task PublishAsync(
        DomainEventMessage message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventType = message.EventType,
            entityId = message.EntityId,
            timestamp = message.Timestamp,
            payload = message.Payload
        });

        Console.WriteLine($"[Kafka] {topic}: {payload}");
        return Task.CompletedTask;
    }
}
