namespace TimeOff.Leave.LeaveRequests.Queries;

/// <summary>
///     Who is away in a period: the approved requests that fall in it, by kind of absence if asked, in
///     date order, a page at a time.
/// </summary>
/// <remarks>
///     <para>
///         "The team" is not a filter here: it is who the caller may see. The requests are read through
///         the same row filter as every other read, so a manager gets their team, an employee their own,
///         and HR everyone — nobody writes the team into the query, and nobody can leave it out.
///     </para>
///     <para>
///         A request is in the period when it ends on or after its first day and starts on or before its
///         last: the two filters cross, <see cref="From" /> against the request's end and
///         <see cref="To" /> against its start.
///     </para>
/// </remarks>
[Query<LeaveRequest, TeamAbsenceDto>(Paged = true)]
[RequirePermission(LeavePermissions.LeaveRequest.Read)]
[Endpoint(HttpVerb.Get, "api/team-calendar")]
public partial class TeamCalendarQuery
{
    /// <summary>The first day of the period.</summary>
    [Filter(MapTo = nameof(LeaveRequest.To), Operator = FilterOperator.GreaterOrEqual)]
    public required DateOnly From { get; init; }

    /// <summary>The last day of the period.</summary>
    [Filter(MapTo = nameof(LeaveRequest.From), Operator = FilterOperator.LessOrEqual)]
    public required DateOnly To { get; init; }

    [Filter]
    public Guid? AbsenceKindId { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? FromSort { get; init; }

    /// <summary>Who is away, not who asked to be: a pending request is not an absence yet.</summary>
    public static Specification<LeaveRequest> Approved => LeaveRequestSpecifications.Approved;
}
