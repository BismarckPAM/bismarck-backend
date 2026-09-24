using System.Text.Json;
using Confluent.Kafka;
using Messaging;

namespace Approval.Service.Services;

public sealed class KafkaDomainEventPublisher : IDomainEventPublisher, IDisposable
{
    private readonly IProducer<string, string> producer;
    private readonly ILogger<KafkaDomainEventPublisher> logger;
    private readonly string defaultTopic;

    public KafkaDomainEventPublisher(
        IConfiguration configuration,
        ILogger<KafkaDomainEventPublisher> logger)
    {
        this.logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        defaultTopic = configuration["Kafka:ApprovalTopic"] ?? KafkaTopics.ApprovalGranted;

        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    public async Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await producer.ProduceAsync(
                string.IsNullOrWhiteSpace(topic) ? defaultTopic : topic,
                new Message<string, string>
                {
                    Key = message.EventId.ToString(),
                    Value = json
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Publishing event {EventType} to topic {Topic} was canceled.",
                message.EventType,
                topic);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to publish event {EventType} to Kafka topic {Topic}.",
                message.EventType,
                topic);
            throw;
        }
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}
