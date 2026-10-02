namespace TimeOff.Leave.Dtos;

/// <summary>
///     An allowance: whose, of what kind, for which year, and how much.
/// </summary>
[MapFrom<Allowance>]
[GenerateProjection]
public partial class AllowanceDto
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public Guid AbsenceKindId { get; init; }

    public int Year { get; init; }

    public decimal Entitled { get; init; }

    public decimal CarriedOver { get; init; }
}
