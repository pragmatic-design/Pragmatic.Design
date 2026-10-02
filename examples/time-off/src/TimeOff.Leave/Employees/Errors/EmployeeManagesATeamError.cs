namespace TimeOff.Leave.Errors;

/// <summary>
///     An employee who manages a team cannot leave until the team has another manager: a team without
///     one has nobody to decide its requests.
/// </summary>
/// <remarks>The words are in <c>translations/*.json</c>, under <c>error.employee.manages.a.team</c>.</remarks>
public sealed partial record EmployeeManagesATeamError : Error
{
    public override string Code => "EMPLOYEE_MANAGES_A_TEAM";
    public override int StatusCode => 409;

    /// <summary>The team, or one of the teams, the employee still manages.</summary>
    public Guid TeamId { get; init; }
}
