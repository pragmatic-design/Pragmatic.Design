namespace TimeOff.Leave.Employees.Queries;

/// <summary>
///     The signed-in employee, as HR registered them.
/// </summary>
/// <remarks>
///     Every employee reads their own profile (<see cref="LeavePermissions.OwnProfile.Manage" />, in every role). The
///     filter is the caller: the invoker fills <see cref="Id" /> from the signed-in employee.
/// </remarks>
[Query<Employee, EmployeeDto>(Single = true)]
[RequirePermission(LeavePermissions.OwnProfile.Manage)]
[Endpoint(HttpVerb.Get, "api/me")]
public partial class GetMyProfileQuery
{
    [FromCurrentUser(nameof(Employee.Id))]
    public Guid Id { get; private set; }
}
