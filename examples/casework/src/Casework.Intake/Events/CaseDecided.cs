using Pragmatic.Events;

namespace Casework.Intake.Events;

/// <summary>
///     A case has been decided. What follows — telling the applicant — is not the deciding.
/// </summary>
/// <remarks>
///     <para>
///         <b>A domain event and not an integration one</b>: it is raised on the entity, captured in the
///         same transaction as the move, and handled <em>in this service</em> after the commit. Nobody
///         outside needs to know how a licence application ended; the applicant does, and that is a
///         letter and a mail rather than a message on a bus.
///     </para>
///     <para>
///         ⚠️ It carries the outcome and the moment, and not the letter: what the applicant receives is
///         composed by the handler from the organisation's template, in the applicant's language, and a
///         decision that carried its own rendered document would have decided both for whoever handles
///         it next.
///     </para>
/// </remarks>
public sealed record CaseDecided(
    Guid CaseId,
    string Number,
    Casework.Verify.Events.VerificationOutcome Outcome,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
