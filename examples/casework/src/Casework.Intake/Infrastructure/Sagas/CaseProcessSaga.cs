using Casework.Intake.Enums;
using Casework.Intake.Events;
using Casework.Verify.Events;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;

namespace Casework.Intake.Infrastructure.Sagas;

/// <summary>
///     The process that carries a case to an end: a verification is asked for, an answer comes back, the
///     case is decided.
/// </summary>
/// <remarks>
///     <para>
///         <b>One thing to read.</b> Before this, "what happens next" was spread across two handlers that
///         each knew a little: one wrote a verification down in another service, one wrote an outcome on
///         a case. Neither could say whether the case was waiting, since when, or what would happen when
///         the answer came. This class says it, the generator turns it into
///         <c>CaseProcessSaga.Orchestrator.g.cs</c>, and the rows in <c>__SagaInstances</c> and
///         <c>__SagaSteps</c> are that statement at run time — which is what
///         <c>TheProcessThatCarriesACase</c> reads with SQL.
///     </para>
///     <para>
///         ⚠️ <b>It lives in Intake and Verify does not know it exists.</b> That is the design and not an
///         accident: the other service answers a request and publishes an outcome, and a saga on its side
///         would make two processes for one conversation. What crosses is a message.
///     </para>
///     <para>
///         ⚠️ <b>A parameterless constructor, and no injected dependencies.</b>
///         <c>EfCoreSagaRepository&lt;TSaga, TState&gt;</c> constrains <c>TSaga</c> to <c>new()</c>, so a
///         saga taking <c>IIntakeInternalActions</c> is <b>CS0310</b> — measured. A saga is persisted
///         data plus decisions; its effects leave as a message (<see cref="DecideTheCase" />) which the
///         orchestrator publishes <em>after</em> committing the saga's state, and which
///         <c>DecideTheCaseWhenTheProcessAsks</c> carries out.
///     </para>
///     <para>
///         ⚠️ <c>[InState(…, NextState = …)]</c> is the <b>default</b> transition, and the branch below
///         relies on it: a step that assigns <c>State</c> itself wins, so <c>WhenTheAnswerArrives</c>
///         chooses <c>Approved</c> or <c>Rejected</c> and the declared <c>NextState</c> never overwrites
///         it. The generated orchestrator applies the default only
///         <c>if (instance.State.Equals(__stateBeforeStep))</c>.
///     </para>
/// </remarks>
[Saga<CaseProcess>]
public partial class CaseProcessSaga : ISaga<CaseProcess>
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public CaseProcess State { get; set; }

    /// <inheritdoc />
    public string CorrelationId { get; set; } = "";

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The case this process is about — the same value as <see cref="CorrelationId" />, typed.</summary>
    public Guid CaseId { get; set; }

    /// <summary>
    ///     What the request asked to be verified, and by when.
    /// </summary>
    /// <remarks>
    ///     Data of the <b>process</b>, not of the case: it is what this conversation is about, kept on
    ///     the saga because it is what the process waits for. Properties a step
    ///     assigns are carried through the repository's save, so they persist with the state.
    /// </remarks>
    public string Kind { get; set; } = "";

    /// <summary>When an answer was expected.</summary>
    public DateTimeOffset ExpectedBy { get; set; }

    /// <summary>
    ///     A verification was asked for: the process begins, waiting, and writes down what it waits for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The one <c>[SagaStart]</c> — several, or none, is <c>PRAG0814</c>. It starts on the
    ///         <b>request</b> and not on the case being opened, because a case with nothing to verify has
    ///         no process to follow: it is a row somebody may still be typing into.
    ///     </para>
    ///     <para>
    ///         It publishes nothing. The request was already published by the case that raised it —
    ///         a step that published it again would send two requests for one case, and this
    ///         saga's job is to <b>know</b> that the case is waiting, not to ask on its behalf.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     ⚠️ <b>The compensation is declared here and not on the step that refuses</b>, and that is the
    ///     whole mechanism: on a <c>SagaRejectedException</c> the orchestrator compensates the steps that
    ///     have <b>already run</b>, reading them back from <c>__SagaSteps</c>. The refusal happens in
    ///     <c>WhenTheAnswerArrives</c>, which throws before its own row is written — so a
    ///     <c>[CompensateWith]</c> there would compensate nothing, while the saga's state would
    ///     still say <c>Compensated</c>. What has an effect to undo is this step: the case went into verification
    ///     and the other service was asked.
    /// </remarks>
    [SagaStart]
    [InState(CaseProcess.AwaitingVerification)]
    [CompensateWith<TheVerificationIsNoLongerWanted>]
    public void WhenAVerificationIsAsked(VerificationRequested requested)
    {
        CaseId = requested.CaseId;
        Kind = requested.Kind;
        ExpectedBy = requested.Deadline;
    }

    /// <summary>
    ///     The answer arrived: the process asks for the case to be decided and ends where the outcome
    ///     says.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The branch is the assignment to <c>State</c>; the case's own move is the operation's, and
    ///         <c>Case.Decide</c> asks its state machine — which refuses a decision on a case that was
    ///         never in verification. So an answer arriving in the wrong state decides nothing twice
    ///         over: the orchestrator will not route it (it dispatches on the current state) and the case
    ///         would refuse it if it did.
    ///     </para>
    ///     <para>
    ///         The returned message is what the orchestrator publishes, and it publishes it <b>after</b>
    ///         the saga's state is committed — so a crash between the two cannot leave a decision carried
    ///         out for a process that never advanced.
    ///     </para>
    /// </remarks>
    [InState(CaseProcess.AwaitingVerification, NextState = CaseProcess.Approved)]
    public DecideTheCase WhenTheAnswerArrives(VerificationAnswered answered)
    {
        // ⚠️ An answer that verified NOTHING is not a third way of deciding: the process cannot act on
        // it, so it gives up. SagaRejectedException is what says so — the orchestrator then walks the
        // compensation chain over the steps that ran (the request), marks the instance Compensated, and
        // does NOT rethrow, so the delivery is not retried. Returning here instead would let the
        // declared NextState advance the process down the happy path and compensation would never fire;
        // throwing anything else would mark it Faulted and retry a refusal that asking three times does
        // not change.
        if (answered.Outcome == VerificationOutcome.Inconclusive)
            throw new SagaRejectedException(
                $"The verification of case {answered.CaseId} came back inconclusive: there is nothing to decide.");

        State = answered.Outcome == VerificationOutcome.Passed
            ? CaseProcess.Approved
            : CaseProcess.Rejected;

        CompletedAt = answered.OccurredAt;

        return new DecideTheCase(answered.CaseId, answered.Outcome);
    }
}
