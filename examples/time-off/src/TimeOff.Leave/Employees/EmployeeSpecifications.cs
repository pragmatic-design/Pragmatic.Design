namespace TimeOff.Leave.Entities;

/// <summary>The rules an employee is read by, beside the generated <c>ById</c> and <c>ByEmployeeNumber</c>.</summary>
public static partial class EmployeeSpecifications
{
    /// <summary>In the team now.</summary>
    public static Specification<Employee> MembersOf(Guid teamId)
        => Spec<Employee>.Where(e => e.TeamId == teamId);

    /// <summary>Who may do anything HR does — the first of them provisions the rest.</summary>
    public static Specification<Employee> HrAdministrators
        => Spec<Employee>.Where(e => e.Role == AccessRole.HrAdministrator);
}
