using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.Identity.Auditing;
using Pragmatic.Identity.Local;
using TimeOff.Leave;
using TimeOff.Leave.Entities;

namespace TimeOff.Host.Identity;

/// <summary>
///     Which employee a failed sign-in or a lockout was about, for the security audit trail.
/// </summary>
/// <remarks>
///     <para>
///         The audit bridge knows the address somebody typed; this application registers its subjects
///         as <c>("Employee", &lt;EmployeeNumber&gt;)</c>, because
///         <c>[DataSubject(nameof(EmployeeNumber))]</c> says so and because an address written into an
///         append-only trail survives every erasure. Nothing but the application can join the two, and
///         this is where it does.
///     </para>
///     <para>
///         ⚠️ A bridge that looked the subject up under a fixed <c>("User", &lt;e-mail&gt;)</c> could
///         never match this registry: every security entry would be written with no subject, and the
///         per-subject rule that finds repeated attempts against <em>one</em> account would raise
///         nothing, ever. An application that configured only that rule would detect no attack of any
///         kind and look correctly configured.
///     </para>
///     <para>
///         <b>In the host and not in the module</b>, like everything else that joins two packages the
///         module never asked for: <c>TimeOff.Leave</c> does not reference <c>Identity.Auditing</c>, and
///         the suite's <c>NoticingAnAttack</c> says why — the module never asked to be audited, the
///         host decided it should be.
///     </para>
///     <para>
///         <b>It returns a key, never a reference.</b> The framework resolves the key through a lookup
///         that never allocates — so an attempt against an address nobody here works under stays
///         unattributed, rather than causing this application to create a pseudonym for whoever typed
///         it.
///     </para>
/// </remarks>
public sealed class TheEmployeeASignInWasAbout(
    [FromKeyedServices(typeof(LeaveBoundary))] DbContext db) : ISecuritySubjectLocator
{
    /// <inheritdoc />
    public async ValueTask<SecuritySubjectKey?> LocateAsync(
        string identity, LoginIdentityKind kind, CancellationToken ct = default)
    {
        var address = kind == LoginIdentityKind.ExternalIdentityKey ? SubjectOf(identity) : identity;

        if (string.IsNullOrWhiteSpace(address))
            return null;

        // The same normalisation the store writes with: one rule for every side of it, or a mixed-case
        // sign-in resolves to nobody and the attack looks unattributable.
        var normalized = LocalIdentity.NormalizeEmail(address);

        // One indexed column — [Unique(nameof(WorkEmail))] — read without tracking and without the
        // query filters, because this runs on the audit path for every failed sign-in, including a
        // burst of them, and a sign-in has no tenant or user to filter by yet.
        var employeeNumber = await db.Set<Employee>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.WorkEmail == normalized)
            .Select(e => e.EmployeeNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(employeeNumber)
            ? null
            : new SecuritySubjectKey(nameof(Employee), employeeNumber);
    }

    /// <summary>
    ///     The subject half of a composed <c>{issuer}|{subject}</c> key — for the local provider, the
    ///     address the account was registered with.
    /// </summary>
    /// <remarks>
    ///     A lockout names the identity record, not what was typed into the form, so the two kinds do
    ///     not arrive in the same shape. Both halves are percent-escaped, which is not decoration: an
    ///     unescaped separator in either would make two principals collide on one key.
    /// </remarks>
    private static string? SubjectOf(string externalIdentityKey)
        => externalIdentityKey.Split(ExternalIdentityKey.Separator) is [_, var subject]
            ? Uri.UnescapeDataString(subject)
            : null;
}
