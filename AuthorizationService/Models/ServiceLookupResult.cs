namespace AuthorizationService.Models;

public sealed record ServiceLookupResult<T>(
    T? Value,
    AuthorizationDenialReason? FailureReason = null);