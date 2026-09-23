using System.Text.Json;
using Confluent.Kafka;

namespace Approval.Service.Services;

public sealed class KafkaDomainEventPublisher : IDomainEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaDomainEventPublisher> _logger;
    private readonly string _topic;

    public KafkaDomainEventPublisher(IConfiguration configuration, ILogger<KafkaDomainEventPublisher> logger)
    {
        _logger = logger;
        
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        _topic = configuration["Kafka:ApprovalTopic"] ?? "approval-events";

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<T>(DomainEventMessage<T> message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var kafkaMessage = new Message<string, string>
            {
                Key = message.AggregateId.ToString(), // Partitions by Aggregate ID
                Value = json
            };

            await _producer.ProduceAsync(_topic, kafkaMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to Kafka topic {Topic}", message.EventType, _topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}