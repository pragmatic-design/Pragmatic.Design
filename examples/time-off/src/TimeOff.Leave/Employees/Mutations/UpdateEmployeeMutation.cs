namespace TimeOff.Leave.Employees.Mutations;

/// <summary>
///     HR changes an employee's name, team, role or preferred language. Only what is sent changes.
/// </summary>
/// <remarks>
///     <para>
///         A new role revokes the employee's sessions: their token still carries the old one, and a
///         token is trusted until it expires. The next request with it is refused, and signing in
///         again issues one that says what is true now.
///     </para>
///     <para>
///         The employee chooses their own language through <c>ChooseMyLanguageAction</c>, which comes
///         here for the change.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.Employee.Update)]
[Endpoint(HttpVerb.Put, "api/employees/{id}")]
[ReturnsDto<EmployeeDto>]
public partial class UpdateEmployeeMutation : Mutation<Employee>
{
    public required Guid Id { get; init; }

    public string? FullName { get; init; }

    public Guid? TeamId { get; init; }

    public AccessRole? Role { get; init; }

    /// <summary>A culture the application speaks: <c>en-US</c>, <c>it-IT</c>.</summary>
    public string? PreferredCulture { get; init; }

    public override Task<Result<Employee, IError>> ApplyAsync(Employee entity, CancellationToken ct = default)
    {
        var language = entity.CheckLanguage();
        if (language.IsFailure)
            return Task.FromResult(Result<Employee, IError>.Failure(language));

        if (entity.ModifiedProperties.Contains(nameof(Employee.Role)))
            entity.RevokeSessions();

        return Task.FromResult(Result<Employee, IError>.Success(entity));
    }
}
