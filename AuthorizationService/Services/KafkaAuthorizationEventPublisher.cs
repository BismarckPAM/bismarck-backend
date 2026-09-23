using System.Text.Json;
using Confluent.Kafka;
using Messaging;

namespace AuthorizationService.Services;

public sealed class KafkaAuthorizationEventPublisher : IAuthorizationEventPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly ILogger<KafkaAuthorizationEventPublisher> logger;

    public KafkaAuthorizationEventPublisher(
        IConfiguration configuration,
        ILogger<KafkaAuthorizationEventPublisher> logger)
    {
        this.logger = logger;
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

        var result = await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = message.EventId.ToString(),
            Value = json
        }, cancellationToken);

        logger.LogInformation(
            "Published authorization event {EventId} to {Topic} at partition {Partition}.",
            message.EventId,
            topic,
            result.Partition.Value);
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}