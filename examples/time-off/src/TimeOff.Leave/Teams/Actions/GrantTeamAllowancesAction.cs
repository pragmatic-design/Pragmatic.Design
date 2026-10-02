using TimeOff.Leave.Allowances.Mutations;

namespace TimeOff.Leave.Teams.Actions;

/// <summary>
///     HR grants every current member of a team their allowance of a kind for a year, in one operation:
///     all of them, or none.
/// </summary>
/// <remarks>
///     <para>
///         Each grant is <see cref="GrantAllowanceMutation" />, invoked through the boundary — the one
///         place that says what a grant is. The grants it stages join this action's unit of work and are
///         written by the one save that closes it, so a failure anywhere in the loop leaves nothing
///         behind, and a grant that races this one to the database fails the whole operation on the
///         unique key rather than half of it.
///     </para>
///     <para>
///         A member who already holds one is the refusal the caller can act on, so it is said up front
///         and for every such member at once (<see cref="AllowanceAlreadyGrantedError" />), instead of
///         surfacing as the database's conflict on the first of them.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.Allowance.Create)]
[ProcessesData<Employee>]
[ProcessesData<Allowance>]
[LoadEntity<Team>(nameof(Id), Include = "Members")]
[Endpoint(HttpVerb.Post, "api/teams/{id}/allowances")]
public partial class GrantTeamAllowancesAction
    : DomainAction<IReadOnlyList<AllowanceDto>, NotFoundError, AllowanceAlreadyGrantedError>
{
    private IReadRepository<Allowance> _allowances = null!;
    private ILeaveInternalActions _leave = null!;

    [FromRoute]
    public required Guid Id { get; init; }

    public required Guid AbsenceKindId { get; init; }

    public required int Year { get; init; }

    public required decimal Entitled { get; init; }

    public override async Task<Result<IReadOnlyList<AllowanceDto>, IError>> Execute(CancellationToken ct = default)
    {
        // The team comes with its members (Include): the rows the filters let through, as any read of them.
        var members = _team.Members
            .OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal)
            .ToList();

        var held = (await _allowances
                .FindAsync(
                    AllowanceSpecifications.HeldByMembersOf(Id) & AllowanceSpecifications.ForKindAndYear(AbsenceKindId, Year),
                    ct)
                .ConfigureAwait(false))
            .Select(a => a.EmployeeId)
            .ToHashSet();
        if (held.Count > 0)
            return new AllowanceAlreadyGrantedError
            {
                EmployeeNumbers = [.. members.Where(m => held.Contains(m.Id)).Select(m => m.EmployeeNumber)]
            };

        var granted = new List<AllowanceDto>(members.Count);
        foreach (var member in members)
        {
            var allowance = await _leave.Allowances
                .GrantAllowance(new GrantAllowanceMutation
                {
                    EmployeeId = member.Id,
                    AbsenceKindId = AbsenceKindId,
                    Year = Year,
                    Entitled = Entitled
                }, ct)
                .ConfigureAwait(false);
            if (allowance.IsFailure)
                return Result<IReadOnlyList<AllowanceDto>, IError>.Failure(allowance.Error);

            granted.Add(AllowanceDto.FromEntity(allowance.Value));
        }

        return granted;
    }
}
