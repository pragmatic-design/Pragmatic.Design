namespace TimeOff.Leave.Employees.Queries;

/// <summary>
///     The people HR looks after, by what HR knows about them — part of a name, an email or a number, a
///     team, who is away today, who has requests waiting — a page at a time. Every filter given narrows
///     the page; none given, it is everyone.
/// </summary>
/// <remarks>
///     The rules about requests are the employee's own (<see cref="Employee.IsAwayOn" />,
///     <see cref="Employee.HasPendingRequests" />), computed by the database: an <c>EXISTS</c> over the
///     requests, not every employee loaded and asked.
/// </remarks>
[Query<Employee, EmployeeDto>(Paged = true)]
[RequirePermission(LeavePermissions.Employee.Read)]
[Endpoint(HttpVerb.Get, "api/employees")]
public partial class SearchEmployeesQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? FullName { get; init; }

    /// <summary>Part of the full name, the work email or the employee number, whatever its case.</summary>
    [SearchAcross(nameof(Employee.FullName), nameof(Employee.WorkEmail), nameof(Employee.EmployeeNumber),
        IgnoreCase = true)]
    public string? Search { get; init; }

    [Filter]
    public Guid? TeamId { get; init; }

    /// <summary>Only who is away today (<c>true</c>), or only who is not (<c>false</c>).</summary>
    [BindSpecification]
    public bool? AwayToday { get; init; }

    /// <summary>Only who has requests waiting for a decision (<c>true</c>), or only who has none (<c>false</c>).</summary>
    [BindSpecification]
    public bool? HasPendingRequests { get; init; }

    /// <summary>
    ///     Today, from the application's clock: the invoker writes it, and no caller can choose which day
    ///     "today" is.
    /// </summary>
    [FromClock]
    public DateOnly Today { get; private set; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? FullNameSort { get; init; }

    public Specification<Employee>? WhoIsAwayToday => AwayToday switch
    {
        true => EmployeeComputedFilters.IsAwayOnSpec(Today),
        false => !EmployeeComputedFilters.IsAwayOnSpec(Today),
        null => null
    };

    public Specification<Employee>? WhoHasPendingRequests => HasPendingRequests switch
    {
        true => EmployeeComputedFilters.HasPendingRequestsSpec,
        false => !EmployeeComputedFilters.HasPendingRequestsSpec,
        null => null
    };
}
