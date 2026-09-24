namespace Notification.Service.DTOs;

public record NotificationResponseDto(
    Guid Id,
    string EventType,
    string Title,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsRead
);