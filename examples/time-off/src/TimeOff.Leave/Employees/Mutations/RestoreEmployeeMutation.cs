namespace TimeOff.Leave.Employees.Mutations;

/// <summary>
///     Undoes a termination recorded by mistake: the employee is back in every read, with their history,
///     and signs in with the password they had.
/// </summary>
[Mutation(Mode = MutationMode.Restore)]
[RequirePermission(LeavePermissions.Employee.Restore)]
[Endpoint(HttpVerb.Post, "api/employees/{id}/restore")]
[ReturnsDto<EmployeeDto>]
public partial class RestoreEmployeeMutation : Mutation<Employee>
{
    public required Guid Id { get; init; }

    public override Task<Result<Employee, IError>> ApplyAsync(Employee entity, CancellationToken ct = default)
    {
        entity.Reinstate();
        return Task.FromResult(Result<Employee, IError>.Success(entity));
    }
}
