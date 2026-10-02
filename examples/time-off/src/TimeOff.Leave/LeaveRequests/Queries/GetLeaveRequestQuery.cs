namespace TimeOff.Leave.LeaveRequests.Queries;

/// <summary>
///     One leave request, if the caller may see it: their own, their team's if they manage it, any if
///     they may see them all. Otherwise it is not found — not forbidden: whether it exists is not
///     theirs to know.
/// </summary>
[Query<LeaveRequest, LeaveRequestDetailDto>(Single = true)]
[RequirePermission(LeavePermissions.LeaveRequest.Read)]
[Endpoint(HttpVerb.Get, "api/leave-requests/{id}")]
public partial class GetLeaveRequestQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
