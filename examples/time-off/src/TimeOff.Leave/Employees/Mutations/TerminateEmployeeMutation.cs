namespace TimeOff.Leave.Employees.Mutations;

/// <summary>
///     HR records that an employee has left: soft-deleted, so gone from every read and restorable, with
///     their history kept. Their account stops authenticating and the sessions already issued end.
/// </summary>
/// <remarks>
///     Refused for a team's manager (<see cref="EmployeeManagesATeamError" />): the team would be left with
///     nobody to decide its requests, and a team read through its manager would disappear with them.
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(LeavePermissions.Employee.Delete)]
[Endpoint(HttpVerb.Delete, "api/employees/{id}")]
public partial class TerminateEmployeeMutation : Mutation<Employee, EmployeeManagesATeamError>
{
    private IReadRepository<Team> _teams = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<Employee, IError>> ApplyAsync(Employee entity, CancellationToken ct = default)
    {
        var managed = await _teams.FirstManagedByOrDefaultAsync(entity.Id, ct).ConfigureAwait(false);
        if (managed is not null)
            return new EmployeeManagesATeamError { TeamId = managed.Id };

        entity.Terminate();
        return entity;
    }
}
