using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TimeOff.Leave.Infrastructure.Privacy;

/// <summary>
///     Erasing an employee closes their account: the sign-in email, the identity key built from it, the
///     password hash, the pending tokens and the last sign-in all go, and the account can no longer
///     authenticate.
/// </summary>
/// <remarks>
///     <para>
///         Written by hand because nothing classifies them. The account is an owned
///         <see cref="Pragmatic.Identity.Local.LocalIdentity" /> in the employee's row, a framework type
///         in another assembly, so the generated erasure plan — which covers the employee's classified
///         properties — never reaches it; and classifying the navigation instead would put the password
///         hash in every export.
///     </para>
///     <para>
///         Before the employee's own step (<c>Order</c> 100), which rewrites the employee number the
///         subject's identity is matched on. The account is removed: its columns go to NULL, as they are
///         for an employee whose account was never opened. A token still in someone's hands stops working
///         with it — the key it names finds no account, so its stamp matches nothing.
///     </para>
///     <para>Registered by the host, as one more erasure step among the generated ones.</para>
/// </remarks>
public sealed class CloseTheAccount(
    [FromKeyedServices(typeof(LeaveBoundary))] DbContext db,
    ISubjectRegistry subjects) : IErasureStep
{
    public string Name => "TimeOff.Leave.Entities.Employee.Identity";

    public int Order => 98;

    public async ValueTask<ErasureStepResult> EraseAsync(string subjectRef, CancellationToken ct = default)
    {
        var employeeNumber = await subjects.ResolveIdentityAsync(subjectRef, ct).ConfigureAwait(false);
        if (employeeNumber is null)
            return ErasureStepResult.Nothing;

        // Past the soft-delete filter: an employee is erased only after they have left, which is exactly
        // the state that hides them.
        var employee = await db.Set<Employee>()
            .IgnoreQueryFilters(["SoftDelete"])
            .FirstOrDefaultAsync(e => e.EmployeeNumber == employeeNumber, ct)
            .ConfigureAwait(false);
        if (employee?.Identity is null)
            return ErasureStepResult.Nothing;

        employee.Identity = null;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ErasureStepResult.Erased(1);
    }
}
