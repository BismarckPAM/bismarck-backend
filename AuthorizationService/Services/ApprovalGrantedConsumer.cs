using System.Text.Json;
using Confluent.Kafka;
using AuthorizationService.Clients;
using AuthorizationService.Data;
using AuthorizationService.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Messaging;

namespace AuthorizationService.Services;

// Strong type representation for the approval-granted metadata payload.
// The Approval service publishes the enriched payload, so the requester id is
// `RequesterUserId` (not the reviewer id carried by the event's Actor).
public record ApprovalGrantedMetadata(
    Guid? ApprovalId,
    Guid? RequesterUserId,
    Guid? UserId,
    Guid? ResourceId,
    int? RequestedLevel,
    int? DurationMinutes,
    string? RequesterName,
    string? RequesterEmail,
    string? ResourceName,
    string? ResourceType,
    string? Action
);

public class ApprovalGrantedConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalGrantedConsumer> _logger;

    public ApprovalGrantedConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<ApprovalGrantedConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield to allow the web application host to complete its startup sequence
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = _configuration["Kafka:GroupId"] ?? "bismarck-authorization-service",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

        // Subscribe to the 'approval-granted' topic
        consumer.Subscribe(KafkaTopics.ApprovalGranted);
        _logger.LogInformation("ApprovalGrantedConsumer subscribed to '{Topic}' with GroupId '{GroupId}'", 
            KafkaTopics.ApprovalGranted, consumerConfig.GroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Consume event
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                // Deserialize SecurityEvent<JsonElement>
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(consumeResult.Message.Value, jsonOptions);

                if (secEvent is null || secEvent.EventId == Guid.Empty)
                {
                    _logger.LogWarning("Malformed event received on topic {Topic} at offset {Offset}. Retrying...",
                        consumeResult.Topic, consumeResult.Offset);
                    throw new FormatException($"Invalid payload at offset {consumeResult.Offset}");
                }

                // Read approval metadata
                var metadata = JsonSerializer.Deserialize<ApprovalGrantedMetadata>(
                    secEvent.Metadata.GetRawText(), jsonOptions);

                // Extract ApprovalId (fallback to EventId if not explicitly placed in metadata)
                var approvalId = metadata?.ApprovalId ?? secEvent.EventId;

                // Extract the REQUESTER id (never the reviewer). Fall back to the
                // Actor envelope only if the requester id is genuinely absent.
                var userId = metadata?.RequesterUserId
                    ?? metadata?.UserId
                    ?? (Guid.TryParse(secEvent.Actor, out var parsedActor) ? parsedActor : Guid.Empty);

                var resourceId = metadata?.ResourceId 
                    ?? (Guid.TryParse(secEvent.Resource, out var parsedRes) ? parsedRes : Guid.Empty);

                int requestedLevel = metadata?.RequestedLevel ?? 1;
                int durationMinutes = metadata?.DurationMinutes ?? 60;

                // Calculate: ExpiresAt = OccurredAt + DurationMinutes
                var grantedAt = secEvent.OccurredAt;
                var expiresAt = grantedAt.AddMinutes(durationMinutes);

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();

                // Check for duplicate ApprovalId (Idempotency)
                bool alreadyExists = await dbContext.TemporaryPermissions
                    .AnyAsync(p => p.ApprovalId == approvalId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("ApprovalId {ApprovalId} already processed. Skipping duplicate insert and committing offset.", approvalId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Insert ACTIVE temporary permission
                var permission = new TemporaryPermission
                {
                    Id = Guid.NewGuid(),
                    ApprovalId = approvalId,
                    UserId = userId,
                    ResourceId = resourceId,
                    RequestedLevel = requestedLevel,
                    GrantedAt = grantedAt,
                    ExpiresAt = expiresAt,
                    Status = TemporaryPermissionStatus.ACTIVE,
                    // Enriched, human-readable context from the approval payload.
                    UserEmail = metadata?.RequesterEmail,
                    ResourceName = metadata?.ResourceName,
                    Action = metadata?.Action
                };

                // Resolve the Azure VM this grant targets so the provisioner can
                // scope the role assignment to the machine, and the console can
                // tell the user how to connect. Advisory: a failure here must not
                // block the grant (falls back to resource-group scope).
                await PopulateVmContextAsync(permission, resourceId, scope, stoppingToken);

                dbContext.TemporaryPermissions.Add(permission);

                // Save to authorization_db
                try
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (DbUpdateException ex)
                {
                    // If Postgres unique constraint (code 23505) fires due to concurrent duplicate
                    if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                    {
                        _logger.LogWarning(
                            "ApprovalId {ApprovalId} was inserted concurrently. Committing duplicate offset.",
                            approvalId);
                    }
                    else
                    {
                        _logger.LogError(
                            ex,
                            "Failed to persist ApprovalGranted event {EventId}. Leaving offset uncommitted.",
                            secEvent.EventId);
                        throw;
                    }
                }

                // Provision the cloud-side role assignment (Azure). Best effort:
                // a failure is recorded on the row and never blocks the session.
                var provisioner = scope.ServiceProvider.GetRequiredService<IAzureJitProvisioner>();
                try
                {
                    var grantResult = await provisioner.GrantAsync(permission, stoppingToken);
                    permission.CloudRoleAssignmentId = grantResult.RoleAssignmentId;
                    permission.ProvisioningStatus = grantResult.Succeeded ? "ACTIVE" : "LOCAL_ONLY";
                    permission.ProvisioningDetail = grantResult.Detail;
                }
                catch (Exception provisionEx) when (provisionEx is not OperationCanceledException)
                {
                    permission.ProvisioningStatus = "FAILED";
                    permission.ProvisioningDetail = "Cloud provisioning failed.";
                    _logger.LogError(provisionEx,
                        "Cloud provisioning failed for PermissionId {PermissionId}.", permission.Id);
                }

                await dbContext.SaveChangesAsync(stoppingToken);

                consumer.Commit(consumeResult);
                _logger.LogInformation(
                    "Processed ApprovalGranted event {EventId} and committed offset.",
                    secEvent.EventId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ApprovalGrantedConsumer cancellation requested.");
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Error processing ApprovalGranted event. Offset remains uncommitted.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    /// <summary>
    /// Best-effort resolution of the Azure VM behind a resource, so the session
    /// can be scoped to the machine and the console can show a connect command.
    /// Never throws: on any failure the grant still proceeds at resource-group
    /// scope, which is strictly broader but still time-boxed and revocable.
    /// </summary>
    private async Task PopulateVmContextAsync(
        TemporaryPermission permission,
        Guid resourceId,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            var resourceClient = scope.ServiceProvider
                .GetService<IResourceServiceClient>();
            if (resourceClient is null)
                return;

            var result = await resourceClient.GetResourceContextAsync(resourceId, null, cancellationToken);
            if (result.Value is null)
                return;

            var resource = result.Value;
            permission.TargetVmName = resource.AzureVmName;
            permission.TargetResourceGroup = resource.AzureResourceGroup;
            permission.TargetHost = resource.PublicHost;
            permission.TargetOsType = resource.OsType;
            permission.ConnectionCommand = BuildConnectionCommand(resource, permission.UserEmail);

            _logger.LogInformation(
                "Resolved VM target for PermissionId {PermissionId}: vm={VmName} rg={ResourceGroup} os={OsType}",
                permission.Id, resource.AzureVmName, resource.AzureResourceGroup, resource.OsType);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception,
                "Could not resolve VM context for resource {ResourceId}; falling back to resource-group scope.",
                resourceId);
        }
    }

    /// <summary>
    /// Builds the command a user should run to reach the VM while the JIT
    /// session is ACTIVE. Access is only actually possible because the
    /// "Virtual Machine User Login" role assignment exists for the duration.
    /// </summary>
    private static string? BuildConnectionCommand(
        AuthorizationService.Clients.ResourceContext resource,
        string? userEmail)
    {
        if (string.IsNullOrWhiteSpace(resource.AzureVmName))
            return null;

        var login = string.IsNullOrWhiteSpace(userEmail)
            ? "<your-azure-email>"
            : userEmail;

        var isWindows = string.Equals(resource.OsType, "Windows", StringComparison.OrdinalIgnoreCase);

        return isWindows
            ? $"az vm ssh -g {resource.AzureResourceGroup} -n {resource.AzureVmName} -l {login}"
            : $"ssh {login}@{resource.PublicHost ?? "<vm-host>"}";
    }
}