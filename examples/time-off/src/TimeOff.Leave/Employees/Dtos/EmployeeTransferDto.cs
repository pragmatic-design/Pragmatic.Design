namespace TimeOff.Leave.Dtos;

/// <summary>
///     A transfer: the team the employee left, the one they are in now, and the requests that went with them.
/// </summary>
public sealed record EmployeeTransferDto(
    Guid EmployeeId, Guid? PreviousTeamId, Guid TeamId, IReadOnlyList<Guid> MovedRequests);
