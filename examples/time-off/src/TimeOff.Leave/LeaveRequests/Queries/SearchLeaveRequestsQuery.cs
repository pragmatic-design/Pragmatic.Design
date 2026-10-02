namespace TimeOff.Leave.LeaveRequests.Queries;

/// <summary>
///     The leave requests the caller may see, by status and by employee, a page at a time — an
///     employee's own, and for a manager their team's: "pending for my team" is this query with
///     <c>status=Pending</c>.
/// </summary>
[Query<LeaveRequest, LeaveRequestDto>(Paged = true)]
[RequirePermission(LeavePermissions.LeaveRequest.Read)]
[Endpoint(HttpVerb.Get, "api/leave-requests")]
public partial class SearchLeaveRequestsQuery
{
    [Filter]
    public LeaveRequestStatus? Status { get; init; }

    [Filter]
    public Guid? EmployeeId { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? FromSort { get; init; }
}
