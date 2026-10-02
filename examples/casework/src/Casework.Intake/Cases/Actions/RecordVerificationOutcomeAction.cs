using Casework.Verify.Events;

namespace Casework.Intake.Cases.Actions;

/// <summary>
///     Writes down the answer a verification came back with — once, whatever the broker does.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>: nobody records an answer over HTTP, it arrives on the bus. This is the
///         boundary's internal surface, which is what the generated <c>IIntakeInternalActions</c> exists
///         for and how a message handler writes — the operation owns the transaction, so the handler has
///         no unit of work to remember.
///     </para>
///     <para>
///         An action and not a mutation, because its inputs are not the case's columns: the event's id
///         is what the case keys its idempotency off, and the outcome's property is named differently
///         from the input. A <c>[Mutation]</c> would map them by name and the generator would be right to
///         complain (PRAG0414).
///     </para>
///     <para>
///         It reports whether it wrote anything, and the handler logs it: "already recorded" is a
///         perfectly good outcome for a redelivery, and a handler that could not tell the two apart
///         would have no way to say so.
///     </para>
/// </remarks>
[DomainAction]
[LoadEntity<Case>(nameof(CaseId), FieldName = "_case")]
public partial class RecordVerificationOutcomeAction : DomainAction<bool, IError>
{
    public required Guid CaseId { get; init; }

    /// <summary>The answer's own id, from the event. What makes this operation idempotent.</summary>
    public required Guid EventId { get; init; }

    public required VerificationOutcome Outcome { get; init; }

    /// <summary>When this service recorded it, from this service's clock.</summary>
    [FromClock]
    public DateTimeOffset RecordedOn { get; private set; }

    public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<bool, IError>>(
            _case.RecordVerificationOutcome(EventId, Outcome, RecordedOn));
}
