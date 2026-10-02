using Pragmatic;

namespace Casework.Verify.Enums;

/// <summary>
///     Where a verification is: asked for, and then answered.
/// </summary>
/// <remarks>
///     <para>
///         Two states and one move, which is all this service's half of the exchange has. It is
///         deliberately <b>not</b> a mirror of the case's <c>CaseStatus</c>: the case is in verification
///         while this is pending, and the two move for different reasons in different databases. A shared
///         enum between the two services would be the coupling this example exists to avoid.
///     </para>
///     <para>
///         ⚠️ No <c>[RaisesEvent&lt;T&gt;]</c> on <c>Answered</c>, and not because nothing is published:
///         the outcome is sent back to Intake, and the event carries the <b>outcome</b>, which is an
///         argument of the answer and not a member the generator could read off the entity
///         (PRAG2751 refuses exactly that). It is raised in the domain method, like Intake's request.
///     </para>
/// </remarks>
[FastEnum]
public enum VerificationStatus
{
    /// <summary>Asked for by a case, and nobody has answered.</summary>
    [InitialState]
    Pending,

    /// <summary>Somebody has answered, and the outcome is on the row.</summary>
    /// <remarks>
    ///     ⚠️ There is deliberately no <c>Withdrawn</c> beside it. When Intake's process gives up it is because
    ///     an answer arrived that it could not act on — so this service has <b>already answered</b>, and
    ///     an answer is a fact it recorded, not an effect to undo. The compensation is Intake's alone:
    ///     its case stops waiting. A state here would be this service unsaying what it found because
    ///     somebody else could not use it.
    /// </remarks>
    [TransitionFrom(VerificationStatus.Pending)]
    Answered
}
