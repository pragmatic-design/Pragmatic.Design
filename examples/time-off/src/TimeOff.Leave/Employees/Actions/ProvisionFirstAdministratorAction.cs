using Pragmatic;

namespace TimeOff.Leave.Employees.Actions;

/// <summary>
///     Creates the first HR administrator, the one account nobody else can create — every other account
///     is created by HR. Does nothing once an HR administrator exists.
/// </summary>
/// <remarks>
///     Not an endpoint. The host runs it at startup from configuration, with no caller: it enters an
///     internal call to do so, which is the framework's way of saying "the application itself".
/// </remarks>
[DomainAction]
public partial class ProvisionFirstAdministratorAction : DomainAction<bool>
{
    private IRepository<Employee> _employees = null!;
    private IPasswordHasher _hasher = null!;
    private IClock _clock = null!;

    public required string FullName { get; init; }

    public required string WorkEmail { get; init; }

    /// <summary>The password the first administrator signs in with. Hashed here and never stored.</summary>
    /// <remarks>
    ///     ⚠️ <c>[NotLogged]</c>, and not <c>[PersonalData]</c>: a password is a secret with no data
    ///     subject — it is not something the administrator has a right to receive a copy of, and it
    ///     is never erased because it is never kept. What it needs is to stay out of the logs, and an
    ///     action's inputs are a typed pipeline object that something will log sooner or later.
    /// </remarks>
    [NotLogged]
    public required string Password { get; init; }

    /// <returns><see langword="true" /> when the administrator was created now.</returns>
    public override async Task<Result<bool, IError>> Execute(CancellationToken ct = default)
    {
        if (await _employees.AnyHrAdministratorsAsync(ct).ConfigureAwait(false))
            return false;

        var administrator = Employee.Create(AccessRole.HrAdministrator, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));
        administrator.SetFullName(FullName);
        administrator.SetWorkEmail(WorkEmail);
        administrator.OpenAccount(_hasher.Hash(Password));

        _employees.Add(administrator);
        return true;
    }
}
