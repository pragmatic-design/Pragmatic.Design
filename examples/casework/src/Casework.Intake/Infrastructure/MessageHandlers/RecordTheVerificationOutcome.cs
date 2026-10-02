using Casework.Verify.Events;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;

namespace Casework.Intake.Infrastructure.MessageHandlers;

/// <summary>
///     Verify answered: this service writes the outcome on the case, once.
/// </summary>
/// <remarks>
///     <para>
///         The other direction of the exchange that <c>VerificationRequested</c> opens, and the reason this example has two services
///         rather than one with a queue in it: the answer arrives when it arrives, into a process that
///         was not waiting for it.
///     </para>
///     <para>
///         ⚠️ <b>Idempotent in the operation, not in the transport.</b> The host calls
///         <c>EnableIdempotency()</c>, and that is worth having, but it is not what makes this safe:
///         its store is in memory (so a restart forgets every id) and it is keyed on the <b>message</b>
///         id, which for an outbox row is the row's id — two publishes of the same event are two rows and
///         two ids. What makes a repeated answer harmless is the case remembering the <c>EventId</c> it
///         already applied. A deduplication window is a period of time; this is a fact in a column.
///     </para>
///     <para>
///         It writes through the boundary's <b>internal</b> interface because a message handler arrives
///         with no principal: the internal one runs as an internal call, and the public one would enforce
///         a permission nobody is there to hold.
///     </para>
/// </remarks>
[MessageHandler]
// An answer that arrives before Intake knows the case is an ordering race: it will succeed on a later
// attempt, and one retry a second later is enough for the window this exchange actually has. An answer
// for a case that never existed exhausts it and dead-letters, which is where somebody looks.
[Redelivery(MaxAttempts = 1, BaseDelaySeconds = 1)]
internal sealed partial class RecordTheVerificationOutcome(
    IIntakeInternalActions intake,
    ILogger<RecordTheVerificationOutcome> logger) : IMessageHandler<VerificationAnswered>
{
    public async Task HandleAsync(
        VerificationAnswered message, MessageContext context, CancellationToken ct = default)
    {
        var recorded = await intake.Cases
            .RecordVerificationOutcome(
                caseId: message.CaseId,
                eventId: message.EventId,
                outcome: message.Outcome,
                ct: ct)
            .ConfigureAwait(false);

        // ⚠️ The operation is loaded through [LoadEntity<Case>], so an answer naming a case this
        // service does not hold fails before anything runs — and a handler that awaited the call and
        // looked at nothing would acknowledge the message and lose an authoritative answer with nothing
        // written anywhere.
        //
        // Throwing rather than logging and acknowledging, because the two failures are indistinguishable
        // from here: an answer that arrived before Intake knows the case is an ordering race and will
        // succeed on a later delivery, one for a case that never existed never will. A nack serves the
        // first and puts the second in the dead letter, where somebody looks. Same as the sibling,
        // DecideTheCaseWhenTheProcessAsks.
        if (recorded.IsFailure)
            throw new InvalidOperationException(
                $"The outcome of case {message.CaseId} could not be recorded: {recorded.Error}");

        // And "already recorded" is a success, not a failure: a redelivery of the same answer writes
        // nothing, which is what the operation reports and what its own documentation says this handler
        // logs. It said so before the handler did.
        if (!recorded.Value)
            LogAlreadyRecorded(message.CaseId, message.EventId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The outcome of case {CaseId} was already recorded from answer {EventId}: nothing was written.")]
    private partial void LogAlreadyRecorded(Guid caseId, Guid eventId);
}
