using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resource.Service.HealthChecks;

/// <summary>
/// Verifies the Kafka broker is reachable by requesting cluster metadata
/// with a short timeout. Does not produce or consume any message — this is
/// a lightweight connectivity probe only, safe to call on every /health hit.
/// </summary>
public class KafkaHealthCheck(string bootstrapServers) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
            using var adminClient = new AdminClientBuilder(config).Build();

            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(3));

            if (metadata.Brokers.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Kafka broker returned metadata with zero brokers."));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Connected to Kafka ({metadata.Brokers.Count} broker(s))."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Unable to reach Kafka broker.", ex));
        }
    }
}