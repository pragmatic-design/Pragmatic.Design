namespace TimeOff.Leave.Entities;

/// <summary>The rules a team is read by, beside the generated <c>ById</c>.</summary>
public static partial class TeamSpecifications
{
    /// <summary>
    ///     Led by the employee. Who manages a team is what a manager's token says and what keeps them from
    ///     being terminated: both read it here (<c>FindManagedByAsync</c>, <c>FirstManagedByOrDefaultAsync</c>).
    /// </summary>
    public static Specification<Team> ManagedBy(Guid employeeId)
        => Spec<Team>.Where(t => t.ManagerId == employeeId);
}
