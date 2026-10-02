using Pragmatic.Events;
using Pragmatic.Audit;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Privacy;

namespace Pragmatic.Identity.Auditing;

/// <summary>
///     Records an account lockout on the audit trail.
/// </summary>
/// <remarks>
///     A lockout always concerns an account that exists, so unlike a failed login it should resolve to a
///     subject. When it does not, the entry is still written without one rather than dropped: an event
///     the trail cannot attribute is a reason to look, not a reason to forget.
/// </remarks>
public sealed class AccountLockedAuditHandler(
    IAuditTrail trail,
    ObservedIdentityResolver identities,
    ISecuritySubjectLocator subjects) : IDomainEventHandler<AccountLocked>
{
    /// <inheritdoc />
    public async Task HandleAsync(AccountLocked @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // ⚠️ The kind is not the same as a failed sign-in's: a lockout names the identity record by its
        // composed {issuer}|{subject} key, not by what was typed. A locator is told which it is given,
        // rather than left to recognise the shape.
        var subjectRef = await SecuritySubject
            .ResolveAsync(subjects, identities, @event.ExternalIdentityKey,
                LoginIdentityKind.ExternalIdentityKey, ct)
            .ConfigureAwait(false);

        await trail.RecordAsync(
            new AuditEntry
            {
                SegmentId = string.Empty,
                OccurredAt = @event.OccurredAt,
                Category = AuditCategory.Security,
                Operation = "Security.AccountLocked",
                SubjectRef = subjectRef,
                // Refused, not failed: the credentials were not rejected, the account was closed to
                // attempts. A reader filtering for genuine authentication failures should not see this.
                Outcome = AuditOutcome.Denied,
                Detail = @event.LockedUntil is { } until
                    ? $"locked until {until:O}"
                    : "locked indefinitely",
            },
            ct).ConfigureAwait(false);
    }
}
