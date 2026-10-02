namespace Casework.Intake.Events;

/// <summary>
///     The process gave up on a verification: undo what asking for it did.
/// </summary>
/// <remarks>
///     <para>
///         The saga's <b>compensation</b> for its start step. A step that throws
///         <c>SagaRejectedException</c> makes the orchestrator walk the compensable steps that already
///         ran and publish each one's compensator; this is the one for
///         <c>WhenAVerificationIsAsked</c>, whose effect was that the case went into verification.
///     </para>
///     <para>
///         ⚠️ <b>Its members have to be named like the saga's, because that is how it is built.</b> The
///         orchestrator serializes the saga instance to JSON and deserializes it into this type — there
///         is no mapping step and no compiler check. A member the saga does not have is seeded with
///         <c>default</c>, and a shape JSON cannot produce at all is a <c>JsonException</c> the
///         generated code catches and logs as "compensation skipped": a compensation that silently does
///         nothing. <c>CaseId</c> and <c>Kind</c> are both properties of <c>CaseProcessSaga</c>, which is
///         what makes this one arrive filled in.
///     </para>
///     <para>
///         A command and not a domain event, for the same reason as <see cref="DecideTheCase" />: it is
///         Intake asking itself to undo something, and nothing outside this service has any business
///         publishing it. Nothing crosses to the other service: Verify has already answered, and the
///         withdrawal is this side's alone (<c>Case.WithdrawVerification</c> raises nothing).
///     </para>
/// </remarks>
/// <param name="CaseId">The case whose verification is withdrawn.</param>
/// <param name="Kind">What had been asked for — carried so the withdrawal can say what it undoes.</param>
public sealed record TheVerificationIsNoLongerWanted(Guid CaseId, string Kind);
