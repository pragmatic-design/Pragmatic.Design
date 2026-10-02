using Casework.Intake.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Casework.Intake.Infrastructure.MessageHandlers;

/// <summary>
///     Carries out the saga's compensation: the case stops waiting for a verification nobody will
///     answer usefully.
/// </summary>
/// <remarks>
///     <para>
///         The mirror of <see cref="DecideTheCaseWhenTheProcessAsks" />, and for the same reason: a saga
///         cannot hold a dependency, so its effects — including undoing one — leave as a message and an
///         operation carries them out.
///     </para>
///     <para>
///         ⚠️ A refusal is not swallowed, exactly as in the decide handler: <c>Case.WithdrawVerification</c>
///         goes through the state machine, so a case that is not in verification refuses the move, the
///         operation returns a failure, and this throws. A compensation that quietly did nothing when it
///         could not run would leave the saga saying <c>Compensated</c> about a case still waiting.
///     </para>
///     <para>
///         <c>[Retry]</c> with every number written out, like the decide handler: the file says what it
///         wants rather than what an engine defaults to.
///     </para>
/// </remarks>
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
internal sealed partial class WithdrawTheVerificationWhenTheProcessGivesUp(IIntakeInternalActions intake)
    : IMessageHandler<TheVerificationIsNoLongerWanted>
{
    public async Task HandleAsync(
        TheVerificationIsNoLongerWanted message, MessageContext context, CancellationToken ct = default)
    {
        var withdrawn = await intake.Cases
            .WithdrawVerification(caseId: message.CaseId, ct: ct)
            .ConfigureAwait(false);

        if (withdrawn.IsFailure)
            throw new InvalidOperationException(
                $"The verification of case {message.CaseId} could not be withdrawn: {withdrawn.Error}");
    }
}
