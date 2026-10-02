namespace TimeOff.Leave.Entities;

/// <summary>
///     The rules a leave request is read by, each written once — and named everywhere else: in the
///     queries, in the actions, and in the balances and filters the entities compute.
/// </summary>
/// <remarks>
///     <para>
///         The other half of the generated <c>LeaveRequestSpecifications</c>, which holds <c>ById</c>: one
///         class per entity, in the entities' namespace, so every file already sees it.
///     </para>
///     <para>
///         Each member is also a read of its own: the generator gives the repository
///         <c>FindApprovedAsync()</c>, <c>FindRequestedByAsync(id)</c>, … and the queryable
///         <c>.Approved()</c>. Composed with <c>&amp;</c>, <c>|</c> and <c>!</c> they stay one query.
///     </para>
///     <para>
///         <see cref="Allowance.Approved" /> and <see cref="Employee.IsAwayOn" /> name them in their bodies,
///         and the database receives the specification's expression: a balance and the filter "away
///         today" say "approved" the way the calendar and the report do, because it is the same rule.
///     </para>
/// </remarks>
public static partial class LeaveRequestSpecifications
{
    /// <summary>Waiting for a decision.</summary>
    public static Specification<LeaveRequest> Pending
        => Spec<LeaveRequest>.Where(r => r.Status == LeaveRequestStatus.Pending);

    /// <summary>Granted: an absence, not a wish.</summary>
    public static Specification<LeaveRequest> Approved
        => Spec<LeaveRequest>.Where(r => r.Status == LeaveRequestStatus.Approved);

    /// <summary>
    ///     Still holding its days — pending or approved. A rejected or withdrawn request holds nothing,
    ///     and asking again for the same days is fine.
    /// </summary>
    public static Specification<LeaveRequest> Standing => Pending | Approved;

    /// <summary>Asked for by the employee.</summary>
    public static Specification<LeaveRequest> RequestedBy(Guid employeeId)
        => Spec<LeaveRequest>.Where(r => r.EmployeeId == employeeId);

    /// <summary>Waiting for a decision, asked for by the employee <paramref name="id" /> names.</summary>
    /// <remarks>
    ///     <c>id</c> and not <c>employeeId</c>: a load binds a rule's parameters by name, and an operation on
    ///     an employee carries its key as <c>Id</c> — the transfer preloads, with this, the requests that
    ///     follow the employee.
    /// </remarks>
    public static Specification<LeaveRequest> PendingOf(Guid id) => RequestedBy(id) & Pending;

    /// <summary>
    ///     Sharing at least one day with <c>[from, to]</c>: it ends on or after the first day, and starts
    ///     on or before the last.
    /// </summary>
    public static Specification<LeaveRequest> Overlapping(DateOnly from, DateOnly to)
        => Spec<LeaveRequest>.Where(r => r.From <= to && r.To >= from);

    /// <summary>Taking <paramref name="day" />.</summary>
    public static Specification<LeaveRequest> Covering(DateOnly day) => Overlapping(day, day);

    /// <summary>Starting in <paramref name="year" />, which is the year it counts in.</summary>
    public static Specification<LeaveRequest> StartingIn(int year)
        => Spec<LeaveRequest>.Where(r => r.From.Year == year);
}
