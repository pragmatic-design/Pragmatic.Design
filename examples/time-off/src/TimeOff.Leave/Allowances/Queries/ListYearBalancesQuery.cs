namespace TimeOff.Leave.Allowances.Queries;

/// <summary>
///     Every employee's allowance of a kind in a year, with what is left of it, computed by the database.
/// </summary>
/// <remarks>
///     Declared once and read where it is needed: the year-end carry-over runs it through the repository
///     it already holds. An employee who has left is not among the rows — the read reaches the employee,
///     and the employee is hidden with them.
/// </remarks>
[Query<Allowance, YearBalanceDto>]
[RequirePermission(LeavePermissions.Allowance.Update)]
public partial class ListYearBalancesQuery
{
    [Filter]
    public required Guid AbsenceKindId { get; init; }

    [Filter]
    public required int Year { get; init; }
}
