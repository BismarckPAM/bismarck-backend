using Audit.Service.Models;

namespace Audit.Service.IntegrationTests;

/// <summary>Builds deterministic <see cref="AuditLog"/> rows for the integration suite.</summary>
internal static class AuditLogEntryBuilder
{
    public static AuditLog Create(
        string actor,
        string? resource = null,
        string eventType = "security.access.granted",
        string outcome = "SUCCESS",
        string action = "GRANT",
        DateTimeOffset? occurredAt = null,
        Guid? id = null)
    {
        return new AuditLog
        {
            Id = id ?? Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OccurredAt = occurredAt ?? DateTimeOffset.UnixEpoch,
            Actor = actor,
            Resource = resource,
            Action = action,
            Outcome = outcome,
            Metadata = "{\"level\":3}",
            ConsumedAt = DateTimeOffset.UnixEpoch
        };
    }
}