namespace TimeOff.Leave.Entities;

/// <summary>
///     What an employee is entitled to of one kind of absence in one year, in the kind's unit — days of
///     vacation, hours of personal leave.
/// </summary>
/// <remarks>
///     <para>
///         One per employee, kind and year: the domain key says so, and the database enforces it.
///     </para>
///     <para>
///         The requests that draw on it are its <see cref="Requests" />, and what is left is computed
///         from them. <c>[Projectable]</c>: a query that reads <see cref="Remaining" /> sends the sum to
///         the database instead of loading the requests. Nothing is stored that the requests already say.
///         Which requests count is <see cref="LeaveRequestSpecifications" />'s to say, here as in every read.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Relation.ManyToOne<Employee>.WithNavigation("Employee")]
[Relation.ManyToOne<AbsenceKind>.WithNavigation("AbsenceKind")]
[Relation.OneToMany<LeaveRequest>.WithNavigation("Requests", Inverse = "Allowance", OnDelete = DeleteBehavior.Restrict)]
[LogicKey("EmployeeId", "AbsenceKindId", nameof(Year))]
public partial class Allowance : IEntity
{
    [Range(2000, 2100)]
    public int Year { get; private set; }

    /// <summary>Granted for the year.</summary>
    [Range(0, 1000)]
    public decimal Entitled { get; private set; }

    /// <summary>Left over from the year before, and usable this year.</summary>
    [Range(0, 1000)]
    public decimal CarriedOver { get; private set; }

    /// <summary>Taken: what the approved requests draw.</summary>
    [Projectable]
    public decimal Approved => Requests.Where(LeaveRequestSpecifications.Approved).Sum(r => r.Amount);

    /// <summary>Asked for and not decided yet: not taken, and not free to ask for again either.</summary>
    [Projectable]
    public decimal Pending => Requests.Where(LeaveRequestSpecifications.Pending).Sum(r => r.Amount);

    /// <summary>What is left of the year: the allowance less what is approved.</summary>
    [Projectable]
    public decimal Remaining => Entitled + CarriedOver - Approved;
}
