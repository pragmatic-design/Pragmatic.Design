namespace TimeOff.Leave.Teams.Mutations;

/// <summary>
///     HR dissolves a team. Its members stay, without a team — the relation says so
///     (<c>OnDelete = SetNull</c> on <see cref="Team" />'s members) and the database does it.
/// </summary>
/// <remarks>
///     The team's leave requests are not touched: their access scopes were stamped when they were asked,
///     so HR still sees them, and they still count against the year.
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(LeavePermissions.Team.Delete)]
[Endpoint(HttpVerb.Delete, "api/teams/{id}")]
public partial class DeleteTeamMutation : Mutation<Team>
{
    public required Guid Id { get; init; }
}
