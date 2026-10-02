namespace TimeOff.Leave.Teams.Mutations;

/// <summary>
///     HR renames a team or hands it to another manager.
/// </summary>
/// <remarks>
///     A new manager revokes the sessions of both managers: the outgoing one's token still says they
///     manage the team, the incoming one's does not say it yet. Both sign in again to a token that is
///     true. The incoming manager is loaded only when one is sent — a manager who does not exist answers
///     404 — and applied here rather than by the automatic mapping, which would leave no trace of who the
///     manager was.
///     <para>
///         The two managers come from two different places, and that is the point: the <b>incoming</b> one
///         is a key the request carries, loaded by <c>[LoadEntity]</c>; the <b>outgoing</b> one is the row
///         the team already points at, which arrives with the team through <c>[EagerLoad]</c>. ⚠️ One option was
///         inferring the second from the first — a load whose key is the FK of a relation the
///         mutation does not write — and on this mutation the rule would have taken the outgoing manager
///         for the incoming one and made the hand-over do nothing, silently. An include is declared.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.Team.Update)]
[Endpoint(HttpVerb.Put, "api/teams/{id}")]
[ReturnsDto<TeamDto>]
[LoadEntity<Employee>(nameof(ManagerId), FieldName = "_incoming")]
[EagerLoad("Manager")]
public partial class UpdateTeamMutation : Mutation<Team>
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }

    [MapIgnore]
    public Guid? ManagerId { get; init; }

    public override Task<Result<Team, IError>> ApplyAsync(Team entity, CancellationToken ct = default)
    {
        if (_incoming is null || _incoming.Id == entity.ManagerId)
            return Task.FromResult<Result<Team, IError>>(entity);

        entity.Manager?.RevokeSessions();
        _incoming.RevokeSessions();

        entity.SetManagerId(_incoming.Id);
        return Task.FromResult<Result<Team, IError>>(entity);
    }
}
