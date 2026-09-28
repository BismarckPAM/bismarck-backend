namespace Approval.Service.DTOs;

/// <summary>
/// The authenticated user's own request history plus aggregate counters,
/// served from the database so it survives browser sessions.
/// GET /api/approval/requests/me -> 200 MyRequestsResponse
/// </summary>
public sealed record MyRequestsResponse(
    IReadOnlyList<ApprovalRequestResponse> Items,
    RequestCounters Counters);

public sealed record RequestCounters(int Total, int Pending, int Approved, int Rejected);