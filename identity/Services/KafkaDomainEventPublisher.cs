using System.Text.Json;
using Confluent.Kafka;
using Messaging;

namespace Identity.Service.Services; 

public sealed class KafkaDomainEventPublisher : IDomainEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaDomainEventPublisher> _logger;

    public KafkaDomainEventPublisher(IConfiguration configuration, ILogger<KafkaDomainEventPublisher> logger)
    {
        _logger = logger;
        
        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<T>(string topic, SecurityEvent<T> message, CancellationToken cancellationToken = default)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(message, jsonOptions);

        try
        {
            // Use EventId or Actor as the Kafka message key for partition distribution
            var result = await _producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = message.EventId.ToString(),
                Value = json
            }, cancellationToken);

            _logger.LogInformation("Published event {EventId} to {Topic} [partition {Partition}]", 
                message.EventId, topic, result.Partition.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to deliver event {EventId} to topic {Topic}", message.EventId, topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}