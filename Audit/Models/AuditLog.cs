namespace Audit.Service.Models;


public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? Resource { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset ConsumedAt { get; set; }
}

