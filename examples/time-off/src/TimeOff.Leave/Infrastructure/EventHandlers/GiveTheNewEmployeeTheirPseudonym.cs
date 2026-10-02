using TimeOff.Leave.Events;

namespace TimeOff.Leave.Infrastructure.EventHandlers;

/// <summary>
///     A newly registered employee gets their subject reference straight away, before they have done
///     anything.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why the moment matters.</b> A reference allocated on first sight — the employee's
///         first <em>successful</em> sign-in, an export, an erasure — would leave an account with no
///         pseudonym between being hired and first signing in, and nothing about it could be
///         attributed. That is precisely the account an attacker works on: a burst of failed sign-ins
///         against a dormant account would be recorded with no subject, and only the rule that counts
///         across the whole system could see it.
///     </para>
///     <para>
///         The bridge that attributes a failed sign-in cannot fix this and must not: its input is an
///         address a stranger typed, so allocating there would let anyone fill the subject registry by
///         guessing, and would have the application create personal data about people as a side effect
///         of rejecting them. The subject has to exist <b>before</b> the attack, not because of it.
///     </para>
///     <para>
///         <b>Why it reads the employee back.</b> It needs the employee, not a field of them:
///         <c>ReferenceAsync</c> is on the entity. It is not a workaround for the event's content: a
///         mutation's declared events are constructed after the save, so a field the save fills —
///         <c>EMP-00001</c>, from a sequence — arrives filled, and taking a value from the event is safe.
///     </para>
///     <para>
///         <c>GetOrCreateReferenceAsync</c> is idempotent, so the three call sites that allocate
///         lazily keep working and cost one lookup. First in the order, because an
///         employee who exists should be attributable before anything else reacts to their arrival.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class GiveTheNewEmployeeTheirPseudonym(
    IReadRepository<Employee> employees,
    ISubjectRegistry subjects) : IDomainEventHandler<EmployeeRegistered>
{
    public int Order => -100;

    public async Task HandleAsync(EmployeeRegistered @event, CancellationToken ct = default)
    {
        var employee = await employees.GetByIdAsync(@event.EmployeeId, ct).ConfigureAwait(false);

        // ⚠️ Not silence: the dispatcher isolates handlers, so a throw here is logged and the hire
        // still succeeds. Saying nothing would leave an employee no failed sign-in can ever be
        // attributed to, and nothing anywhere would record why.
        if (employee is null)
            throw new InvalidOperationException(
                $"Employee {@event.EmployeeId} was registered and could not be read back, so no subject "
                + "reference was allocated for them.");

        await employee.ReferenceAsync(subjects, ct).ConfigureAwait(false);
    }
}
