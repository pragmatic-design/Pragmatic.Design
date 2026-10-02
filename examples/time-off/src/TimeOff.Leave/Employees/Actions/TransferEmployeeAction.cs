using TimeOff.Leave.Employees.Mutations;

namespace TimeOff.Leave.Employees.Actions;

/// <summary>
///     HR moves an employee to another team. The requests still waiting for a decision go with them to
///     the new manager; what was already decided stays where it was decided.
/// </summary>
/// <remarks>
///     <para>
///         The team changes through <see cref="UpdateEmployeeMutation" />, the one place that says what an
///         update of an employee is. Each pending request — preloaded by the rule that says which ones,
///         <see cref="LeaveRequestSpecifications.PendingOf" /> — moves through <see cref="LeaveRequest.FollowToTeam" />
///         — the entity, not a bulk update — so each is a change of its own in the audit trail, and all of
///         it is committed together by this operation.
///     </para>
///     <para>
///         The employee's sessions go on: a member's token carries their access role and the teams they
///         manage, never the team they belong to, so the move changes nothing the token says. Moving to
///         the team they are already in changes nothing at all, and answers so.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.Employee.Update)]
[ProcessesData<Employee>]
[ProcessesData<LeaveRequest>]
[LoadEntity<Employee>(nameof(Id))]
[LoadEntity<Team>(nameof(TeamId))]
[LoadEntities<LeaveRequest>(Specification = nameof(LeaveRequestSpecifications.PendingOf), FieldName = "_pending")]
[Endpoint(HttpVerb.Post, "api/employees/{id}/transfer")]
public partial class TransferEmployeeAction : DomainAction<EmployeeTransferDto, NotFoundError>
{
    private ILeaveInternalActions _leave = null!;

    [FromRoute]
    public required Guid Id { get; init; }

    public required Guid TeamId { get; init; }

    public override async Task<Result<EmployeeTransferDto, IError>> Execute(CancellationToken ct = default)
    {
        var previousTeam = _employee.TeamId;
        if (previousTeam == TeamId)
            return new EmployeeTransferDto(Id, previousTeam, TeamId, []);

        var updated = await _leave.Employees
            .UpdateEmployee(new UpdateEmployeeMutation { Id = Id, TeamId = TeamId }, _employee, ct)
            .ConfigureAwait(false);
        if (updated.IsFailure)
            return Result<EmployeeTransferDto, IError>.Failure(updated.Error);

        var moved = new List<Guid>(_pending.Count);
        foreach (var request in _pending)
        {
            if (request.FollowToTeam(TeamId))
                moved.Add(request.Id);
        }

        return new EmployeeTransferDto(Id, previousTeam, TeamId, moved);
    }
}
