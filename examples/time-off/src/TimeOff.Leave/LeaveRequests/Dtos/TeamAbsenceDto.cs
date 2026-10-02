namespace TimeOff.Leave.Dtos;

/// <summary>
///     Someone away: who, of what kind, and when — a line of the team calendar.
/// </summary>
/// <remarks>
///     <see cref="EmployeeFullName" /> is read through the request's employee, in the same query: the
///     projection joins, nothing is loaded per line.
/// </remarks>
[MapFrom<LeaveRequest>]
[GenerateProjection]
public partial class TeamAbsenceDto
{
    /// <summary>The leave request.</summary>
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }

    public string EmployeeFullName { get; init; } = "";

    public Guid AbsenceKindId { get; init; }

    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    /// <summary>For a kind counted in hours: how many, on the one day.</summary>
    public decimal? Hours { get; init; }
}
