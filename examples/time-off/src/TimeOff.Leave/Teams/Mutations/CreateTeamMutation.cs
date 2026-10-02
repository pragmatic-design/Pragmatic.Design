namespace TimeOff.Leave.Teams.Mutations;

/// <summary>
///     HR creates a team and names its manager, who decides its members' leave requests.
/// </summary>
/// <remarks>
///     The manager is loaded before the team is created — a manager who does not exist answers 404 — and
///     their sessions are revoked: their token does not say they manage the new team, and the next sign-in
///     issues one that does.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(LeavePermissions.Team.Create)]
[Endpoint(HttpVerb.Post, "api/teams")]
[CreatedAt("/api/teams/{Id}")]
[ReturnsDto<TeamDto>]
[LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]
public partial class CreateTeamMutation : Mutation<Team>
{
    public required string Name { get; init; }

    public required Guid ManagerId { get; init; }

    public override Task<Result<Team, IError>> ApplyAsync(Team entity, CancellationToken ct = default)
    {
        _manager.RevokeSessions();
        return Task.FromResult<Result<Team, IError>>(entity);
    }
}
