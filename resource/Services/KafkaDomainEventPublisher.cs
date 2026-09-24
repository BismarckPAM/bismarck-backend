using System.Text.Json;
using Confluent.Kafka;
using Messaging;

namespace Resource.Service.Services;

public sealed class KafkaDomainEventPublisher : IDomainEventPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly ILogger<KafkaDomainEventPublisher> logger;
    private readonly string defaultTopic;

    public KafkaDomainEventPublisher(IConfiguration configuration, ILogger<KafkaDomainEventPublisher> logger)
    {
        this.logger = logger;
        defaultTopic = configuration["Kafka:Topic"] ?? "resource-events";
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    public async Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var targetTopic = string.IsNullOrWhiteSpace(topic) ? defaultTopic : topic;
        await producer.ProduceAsync(targetTopic, new Message<string, string>
        {
            Key = message.EventId.ToString(),
            Value = json
        }, cancellationToken);
        logger.LogInformation("Published event {EventId} to {Topic}", message.EventId, targetTopic);
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}
