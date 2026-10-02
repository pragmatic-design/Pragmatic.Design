namespace TimeOff.Leave.Allowances.Queries;

/// <summary>
///     The signed-in employee's balance of each kind for a year — granted, taken, pending, and left —
///     computed by the database.
/// </summary>
/// <remarks>
///     Every employee reads their own (<see cref="LeavePermissions.OwnProfile.Manage" />, in every role). Whose balances
///     they are is the caller: the invoker fills <see cref="EmployeeId" /> from the signed-in employee,
///     and nothing the request carries can change it — not the query string, not an in-process caller.
/// </remarks>
[Query<Allowance, AllowanceBalanceDto>]
[RequirePermission(LeavePermissions.OwnProfile.Manage)]
[Endpoint(HttpVerb.Get, "api/me/balances")]
public partial class GetMyBalancesQuery
{
    [FromCurrentUser(nameof(Employee.Id))]
    public Guid EmployeeId { get; private set; }

    [Filter]
    public required int Year { get; init; }
}
