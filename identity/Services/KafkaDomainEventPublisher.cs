using System.Text.Json;

namespace Identity.Service.Services;

public sealed class KafkaDomainEventPublisher : IDomainEventPublisher
{
    private readonly string _bootstrapServers;
    private readonly string _topic;

    public KafkaDomainEventPublisher(IConfiguration configuration)
    {
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _topic = configuration["Kafka:Topic"] ?? "bismarck.domain-events";
    }

    public Task PublishAsync(DomainEventMessage message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventType = message.EventType,
            entityId = message.EntityId,
            timestamp = message.Timestamp,
            payload = message.Payload
        });

        Console.WriteLine($"[Kafka] {_topic}: {payload}");
        return Task.CompletedTask;
    }
}
