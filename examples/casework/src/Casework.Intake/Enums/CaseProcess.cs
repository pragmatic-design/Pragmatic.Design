namespace Casework.Intake.Enums;

/// <summary>
///     Where the <b>process</b> that carries a case to an end has got to.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Not the same thing as <c>CaseStatus</c>, and the difference is the point. The
///         status is what the case <b>is</b>, written by the case's own state machine and readable by
///         anybody who reads the row. This is where the <b>conversation</b> has got to — one instance per
///         case, in <c>__SagaInstances</c>, with a row per step in <c>__SagaSteps</c>. A process can be
///         waiting while the case is unchanged, and a case can be decided by something that is not this
///         process at all.
///     </para>
///     <para>
///         The two look alike here because this process is short. An expiry and a compensation are moves
///         of the case (<c>Expired</c>, and back to <c>Open</c>) and add no state here.
///     </para>
///     <para>
///         ⚠️ An <b>enum</b> because <c>[Saga&lt;TState&gt;]</c> constrains its type argument to
///         <c>struct, Enum</c>. There is no <c>[FastEnum]</c> on it: it is persisted by the saga
///         repository as its underlying value and never travels on the wire.
///     </para>
/// </remarks>
public enum CaseProcess
{
    /// <summary>A verification has been asked for and the process is waiting for its answer.</summary>
    AwaitingVerification,

    /// <summary>The answer arrived and the case was approved.</summary>
    Approved,

    /// <summary>The answer arrived and the case was rejected.</summary>
    Rejected
}
