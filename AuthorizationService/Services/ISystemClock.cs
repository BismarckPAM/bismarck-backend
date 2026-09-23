namespace AuthorizationService.Services;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}