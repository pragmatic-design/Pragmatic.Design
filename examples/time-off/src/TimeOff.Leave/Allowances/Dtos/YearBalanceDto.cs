namespace TimeOff.Leave.Dtos;

/// <summary>
///     An employee's allowance of a kind in a year, with what is left of it and what is asked for and not
///     decided yet — the row a year-end carry-over reads.
/// </summary>
/// <remarks>
///     A projection: <see cref="Pending" /> and <see cref="Remaining" /> are the allowance's
///     <c>[Projectable]</c> members, so the database sums the requests in the query that reads the row.
/// </remarks>
[MapFrom<Allowance>]
[GenerateProjection]
public partial class YearBalanceDto
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    [MapProperty("Employee.EmployeeNumber")]
    public string EmployeeNumber { get; init; } = "";

    public decimal Pending { get; init; }

    public decimal Remaining { get; init; }
}
