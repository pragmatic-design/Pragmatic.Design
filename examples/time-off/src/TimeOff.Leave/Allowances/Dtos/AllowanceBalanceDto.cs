namespace TimeOff.Leave.Dtos;

/// <summary>
///     What is left of an allowance: granted, carried over, taken, asked for and not decided, and left.
/// </summary>
/// <remarks>
///     A projection: <see cref="Approved" />, <see cref="Pending" /> and <see cref="Remaining" /> are the
///     allowance's <c>[Projectable]</c> members, so the database computes them in the query that reads
///     the row.
/// </remarks>
[MapFrom<Allowance>]
[GenerateProjection]
public partial class AllowanceBalanceDto
{
    public Guid Id { get; init; }

    public Guid AbsenceKindId { get; init; }

    public int Year { get; init; }

    public decimal Entitled { get; init; }

    public decimal CarriedOver { get; init; }

    public decimal Approved { get; init; }

    public decimal Pending { get; init; }

    public decimal Remaining { get; init; }
}
