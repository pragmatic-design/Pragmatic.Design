using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Casework.Verify.Events;

/// <summary>
///     A verification has been answered. Published by Verify; Intake records it on the case.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The namespace is load-bearing and is not the project's name</b>, for the same reason as
///         Intake's contract: the topic comes from the message's namespace's second segment, so this one
///         is published to <c>verify.events</c>. A project called <c>Casework.Contracts</c> with a
///         matching namespace would publish to <c>contracts.events</c> — a topic named after a build
///         artefact.
///     </para>
///     <para>
///         It inherits <c>DomainEvent</c>, which is what gives it an <c>EventId</c>. The consumer keys its
///         write off that id, so a message delivered twice changes
///         the case once. ⚠️ Implementing <c>IDomainEvent</c> by hand instead would leave <c>EventId</c>
///         at <c>Guid.Empty</c> — every message carrying the same id, which turns an idempotent handler
///         into one that applies the first outcome and ignores every later one.
///     </para>
///     <para>
///         The <b>reason</b> is not on it, and that is deliberate: nothing in Intake reads one, and an
///         event carrying a field nobody reads is a promise nobody keeps. What crosses is which
///         verification, which case, and what it found.
///     </para>
/// </remarks>
/// <param name="VerificationId">Verify's own row, for correlation and for a support question later.</param>
/// <param name="CaseId">The case in Intake's database this answer belongs to.</param>
/// <param name="Outcome">What the verification found.</param>
/// <param name="OccurredAt">When it was answered. Verify's clock, not the broker's.</param>
/// <remarks>
///     ⚠️ <c>[CorrelationKey]</c> on the <b>case</b> and not on the verification: the conversation
///     this message belongs to is the case's, which is what Intake's saga follows. Keying it on
///     <c>VerificationId</c> would be a different conversation — one per verification.
/// </remarks>
public sealed record VerificationAnswered(
    Guid VerificationId,
    [property: CorrelationKey] Guid CaseId,
    VerificationOutcome Outcome,
    DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;
