using Pragmatic.Events;
using Pragmatic.Audit;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Privacy;

namespace Pragmatic.Identity.Auditing;

/// <summary>
///     Records a failed login on the audit trail, pseudonymised.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="LoginFailed" /> carries the attempted address in plaintext, deliberately, so that
///         security tooling can correlate attempts. Its own documentation says whoever persists it owns
///         the data-protection obligations that follow. This is that owner.
///     </para>
///     <para>
///         <b>The address never reaches the trail.</b> It is resolved to a subject reference through
///         <see cref="ObservedIdentityResolver" />, which looks up and never allocates: an attempt
///         against a known account carries that account's pseudonym and can be correlated with its
///         history; an attempt against an identity nobody recognises is recorded with <b>no subject at
///         all</b>. Pseudonymising the attempted address instead would let anyone fill the subject
///         registry by guessing, and would have the system create personal data about people as a side
///         effect of rejecting them.
///     </para>
///     <para>
///         An unattributable failure is still recorded. It is a security signal, and often the more
///         interesting one — a burst of attempts against accounts that do not exist is what enumeration
///         looks like.
///     </para>
/// </remarks>
public sealed class LoginFailedAuditHandler(
    IAuditTrail trail,
    ObservedIdentityResolver identities,
    ISecuritySubjectLocator subjects) : IDomainEventHandler<LoginFailed>
{
    /// <inheritdoc />
    public async Task HandleAsync(LoginFailed @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // ⚠️ Which subject this is about is the application's to answer, not a const on this class such
        // as ("User", <the e-mail>). An application that registers its subjects any other way — which
        // [DataSubject(nameof(EmployeeNumber))] asks it to — could never match a const, so every entry
        // would carry no subject and per-subject correlation would find nothing, ever.
        var subjectRef = await SecuritySubject
            .ResolveAsync(subjects, identities, @event.Email, LoginIdentityKind.LoginName, ct)
            .ConfigureAwait(false);

        await trail.RecordAsync(
            new AuditEntry
            {
                SegmentId = string.Empty,          // assigned by the trail
                OccurredAt = @event.OccurredAt,
                Category = AuditCategory.Security,
                Operation = "Security.LoginFailed",
                SubjectRef = subjectRef,
                Outcome = AuditOutcome.Failed,
                // The reason, never the address. The trail redacts the detail on the way in as well,
                // but a value that is not put there cannot be redacted wrongly.
                Detail = @event.Reason,
            },
            ct).ConfigureAwait(false);
    }
}
