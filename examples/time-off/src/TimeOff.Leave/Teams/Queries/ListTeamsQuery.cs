namespace TimeOff.Leave.Teams.Queries;

/// <summary>
///     The teams, by name.
/// </summary>
[Query<Team, TeamDto>(Paged = true)]
[RequirePermission(LeavePermissions.Team.Read)]
[Endpoint(HttpVerb.Get, "api/teams")]
public partial class ListTeamsQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Name { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NameSort { get; init; }
}
