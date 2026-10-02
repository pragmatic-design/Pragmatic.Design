using Pragmatic.Identity.Local.Actions;
using TimeOff.Leave.Events;

namespace TimeOff.Leave.Infrastructure.EventHandlers;

/// <summary>
///     Invites a newly registered employee to choose their password.
/// </summary>
/// <remarks>
///     <para>
///         The invitation is a password reset: a single-use, short-lived token delivered out of band by
///         the registered <c>IPasswordResetNotifier</c>, which the employee spends on
///         <c>/identity/local/reset-password/confirm</c>. One mechanism for "first password" and
///         "forgotten password", and neither ever puts a password or a token in a response.
///     </para>
///     <para>
///         After the commit, as every event handler runs: the account the reset looks up has to be in
///         the database already.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class InviteTheNewEmployee(ILeaveInternalActions leave) : IDomainEventHandler<EmployeeRegistered>
{
    public async Task HandleAsync(EmployeeRegistered @event, CancellationToken ct = default)
    {
        var result = await leave.Identity
            .RequestPasswordReset(new RequestPasswordReset { Email = @event.WorkEmail }, ct)
            .ConfigureAwait(false);

        if (result.IsFailure)
            throw new InvalidOperationException(
                $"The invitation for employee {@event.EmployeeId} could not be issued: {result.Error.Code}");
    }
}
