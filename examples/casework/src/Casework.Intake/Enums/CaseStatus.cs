using Pragmatic;

namespace Casework.Intake.Enums;

/// <summary>
///     Where a case is in its life: open, waiting for a verification, then decided one way or the other.
/// </summary>
/// <remarks>
///     <para>
///         Every move that exists is declared here, and the generated <c>TransitionTo</c> refuses any
///         other — which is why no operation carries an <c>if</c> about the status. A case cannot be
///         approved without a verification, and that is not a rule written in an operation: it is the
///         absence of a move from <c>Open</c> to <c>Approved</c>.
///     </para>
///     <para>
///         ⚠️ No <c>[RaisesEvent&lt;T&gt;]</c> on <c>InVerification</c>, and the reason is worth knowing
///         before somebody adds one: the event of that move carries the <b>kind</b> of verification asked
///         for, which is an argument of the call and not a member of the case, so the generator would
///         fill it with <c>default</c> and say so (PRAG2751). An event whose data is not on the entity is
///         raised in the domain method, which is what <c>Case.AskForVerification</c> does.
///     </para>
/// </remarks>
[FastEnum]
public enum CaseStatus
{
    /// <summary>Somebody has asked for something. Nothing has been checked yet.</summary>
    /// <remarks>
    ///     ⚠️ Reachable from <c>InVerification</c>, and that move is the saga's
    ///     <b>compensation</b>: an answer the process cannot act on withdraws the request, and a case
    ///     that is no longer waiting has to be somewhere an operator can act on. Back to <c>Open</c>
    ///     rather than a state of its own, because it is the same thing it was before anybody asked —
    ///     a withdrawn verification leaves no trace on the status, and what happened is on the row's
    ///     <c>VerificationAskedOn</c> and in the saga's steps.
    /// </remarks>
    [InitialState]
    [TransitionFrom(CaseStatus.InVerification)]
    Open,

    /// <summary>A verification has been asked for and has not answered.</summary>
    /// <remarks>
    ///     Reachable from <c>Expired</c> too: a verification nobody answered can be asked
    ///     for again. Which is the whole reason expiring is a state and not a deletion — the case goes
    ///     back to something an operator can act on.
    /// </remarks>
    [TransitionFrom(CaseStatus.Open)]
    [TransitionFrom(CaseStatus.Expired)]
    InVerification,

    /// <summary>
    ///     The verification's deadline passed and nobody answered.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A state of the <b>case</b>, reached by the recurring deadline sweep and by nothing else.
    ///     It exists so that a case cannot sit in <c>InVerification</c> for ever because a message never
    ///     came — and being a state rather than a flag is what makes the answer that arrives afterwards
    ///     refusable: there is no move from <c>Expired</c> to <c>Approved</c>, so a late outcome is
    ///     recorded as a fact and decides nothing.
    /// </remarks>
    [TransitionFrom(CaseStatus.InVerification)]
    Expired,

    /// <summary>Verified, and decided in the applicant's favour.</summary>
    [TransitionFrom(CaseStatus.InVerification)]
    Approved,

    /// <summary>Decided against, and the reason is on the case.</summary>
    [TransitionFrom(CaseStatus.InVerification)]
    Rejected
}
