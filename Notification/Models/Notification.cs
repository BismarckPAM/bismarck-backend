namespace Notification.Service.Models;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; } = string.Empty; 
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRead { get; set; }
}