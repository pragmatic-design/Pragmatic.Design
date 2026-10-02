namespace TimeOff.Leave.Entities;

/// <summary>The rules an allowance is read by, beside the generated <c>ById</c>.</summary>
public static partial class AllowanceSpecifications
{
    /// <summary>Of one kind of absence, for one year.</summary>
    public static Specification<Allowance> ForKindAndYear(Guid absenceKindId, int year)
        => Spec<Allowance>.Where(a => a.AbsenceKindId == absenceKindId && a.Year == year);

    /// <summary>
    ///     Held by someone in the team now: through the employee, so the database joins instead of the
    ///     caller loading the members and sending their ids back.
    /// </summary>
    public static Specification<Allowance> HeldByMembersOf(Guid teamId)
        => Spec<Allowance>.Where(a => a.Employee.TeamId == teamId);
}
