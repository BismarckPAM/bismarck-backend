using Audit.Service.Models;

namespace Audit.Service.Tests;

/// <summary>Builds deterministic <see cref="AuditLog"/> rows for the unit suite.</summary>
internal static class AuditLogFactory
{
    public static AuditLog Create(
        string actor,
        string? resource = null,
        string eventType = "security.access.granted",
        string outcome = "SUCCESS",
        string action = "GRANT",
        DateTimeOffset? occurredAt = null,
        Guid? id = null,
        Guid? eventId = null)
    {
        return new AuditLog
        {
            Id = id ?? Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            EventType = eventType,
            OccurredAt = occurredAt ?? DateTimeOffset.UnixEpoch,
            Actor = actor,
            Resource = resource,
            Action = action,
            Outcome = outcome,
            Metadata = "{}",
            ConsumedAt = DateTimeOffset.UnixEpoch
        };
    }
}