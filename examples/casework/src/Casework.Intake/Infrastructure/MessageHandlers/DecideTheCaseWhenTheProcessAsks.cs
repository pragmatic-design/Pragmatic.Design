using Casework.Intake.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Casework.Intake.Infrastructure.MessageHandlers;

/// <summary>
///     Carries out what the saga decided: the case is approved or rejected.
/// </summary>
/// <remarks>
///     <para>
///         The saga chose; this runs the operation. The two are separate because a saga cannot hold a
///         dependency (see <see cref="DecideTheCase" />), and separating them has a second effect worth
///         having: the decision is committed with the saga's own state, and carrying it out is a message
///         that can be retried on its own.
///     </para>
///     <para>
///         ⚠️ A refusal is not swallowed. <c>Case.Decide</c> goes through the state machine, so a case
///         that is not in verification refuses the move, the operation returns a failure, and this
///         handler throws — which lets the delivery pipeline retry and, eventually, dead-letter it. A
///         handler that ignored the failure would leave the saga saying "approved" about a case that is
///         not.
///     </para>
///     <para>
///         <c>[Retry]</c>: three attempts in total — <b>including the first</b> — with an
///         exponential delay and jitter, and then the dead letter. Declared on the handler because it is
///         a property of <em>this</em> reaction and not of the bus: a transient database failure deserves
///         a second try, and a decision the state machine will refuse for ever does not become allowed by
///         asking three times. Both go the same way here, which is the honest cost of the message not
///         being able to say which it is.
///     </para>
///     <para>
///         Every number is written out. The defaults would apply otherwise, and writing the values is how
///         this file says what it wants rather than what an engine defaults to.
///     </para>
/// </remarks>
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
internal sealed partial class DecideTheCaseWhenTheProcessAsks(IIntakeInternalActions intake)
    : IMessageHandler<DecideTheCase>
{
    public async Task HandleAsync(
        DecideTheCase message, MessageContext context, CancellationToken ct = default)
    {
        var decided = await intake.Cases
            .DecideCase(caseId: message.CaseId, outcome: message.Outcome, ct: ct)
            .ConfigureAwait(false);

        if (decided.IsFailure)
            throw new InvalidOperationException(
                $"The case {message.CaseId} could not be decided: {decided.Error}");
    }
}
