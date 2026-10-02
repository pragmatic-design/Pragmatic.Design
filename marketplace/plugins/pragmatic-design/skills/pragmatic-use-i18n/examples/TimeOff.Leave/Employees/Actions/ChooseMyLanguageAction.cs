using Pragmatic.Internationalization.Context;
using TimeOff.Leave.Employees.Mutations;

namespace TimeOff.Leave.Employees.Actions;

/// <summary>
///     The signed-in employee chooses the language the application answers them in when a request
///     names none: <c>en-US</c> or <c>it-IT</c>.
/// </summary>
/// <remarks>
///     <para>
///         Every employee sets their own (<see cref="LeavePermissions.OwnProfile.Manage" />, in every role). The change
///         itself is <see cref="UpdateEmployeeMutation" />, reached as an internal call: one place
///         checks the language and writes it, whoever asks.
///     </para>
///     <para>
///         The profile's culture is cached per employee by the generated
///         <see cref="EmployeeCultureConfigProvider" />; the entry is dropped here, so the next request
///         answers in the new one.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.OwnProfile.Manage)]
[ProcessesData<Employee>]
[LoadCurrentUser]
[Endpoint(HttpVerb.Put, "api/me/language")]
public partial class ChooseMyLanguageAction : DomainAction<EmployeeDto, NotFoundError>
{
    private ILeaveInternalActions _leave = null!;
    private IEnumerable<II18NConfigProvider> _languageSources = null!;

    public required string Language { get; init; }

    public override async Task<Result<EmployeeDto, IError>> Execute(CancellationToken ct = default)
    {
        var updated = await _leave.Employees
            .UpdateEmployee(new UpdateEmployeeMutation { Id = _currentEmployee.Id, PreferredCulture = Language }, ct)
            .ConfigureAwait(false);
        if (updated.IsFailure)
            return Result<EmployeeDto, IError>.Failure(updated.Error);

        foreach (var profile in _languageSources.OfType<EmployeeCultureConfigProvider>())
            profile.InvalidateCache();

        return EmployeeDto.FromEntity(updated.Value);
    }
}
