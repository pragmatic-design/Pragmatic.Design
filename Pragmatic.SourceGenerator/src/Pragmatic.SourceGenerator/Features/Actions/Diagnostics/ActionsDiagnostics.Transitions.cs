using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Diagnostics;

/// <summary>
///     <c>[TransitionsTo]</c>: the transition the invoker performs, and the declarations it cannot
///     perform as written.
/// </summary>
internal static partial class ActionsDiagnostics
{
    /// <summary>PRAG0465: the invoker cannot tell which entity to move.</summary>
    /// <remarks>
    ///     The mutation's entity, or the one <c>[LoadEntity]</c> row on an action, must carry
    ///     <c>[StateMachine&lt;TState&gt;]</c> of the attribute's enum. None is a declaration nothing can
    ///     perform; more than one on an action is a choice the generator will not guess.
    /// </remarks>
    public static readonly DiagnosticDescriptor TransitionHasNoEntity = new(
        "PRAG0465",
        "[TransitionsTo] names no entity the invoker can move",
        "'{0}' declares [TransitionsTo<{1}>] but {2}. The invoker moves the mutation's entity, or the single entity loaded with [LoadEntity] whose [StateMachine<{1}>] it is.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>PRAG0466: the body transitions to the target the invoker already transitions to.</summary>
    /// <remarks>
    ///     The second call is a move from the target to itself, which the state machine refuses: a 409 on
    ///     every call, certain from the source. It was once the only correct form, so older operations
    ///     carry the call.
    /// </remarks>
    public static readonly DiagnosticDescriptor BodyAlsoTransitions = new(
        "PRAG0466",
        "The body transitions to the state the invoker already moves the entity to",
        "'{0}' declares [TransitionsTo] {1} and its body also calls TransitionTo({1}): the second call is refused as a move from '{1}' to itself, so every call answers 409. Remove the call from the body, or declare When = TransitionTiming.ByBody if the body is where the transition belongs.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>PRAG0467: <c>AfterBody</c> on a domain action.</summary>
    /// <remarks>
    ///     An action builds its response in the body — <c>XxxDto.FromEntity(entity)</c> — so a transition
    ///     after it would answer with the state before the move.
    /// </remarks>
    public static readonly DiagnosticDescriptor AfterBodyOnAnAction = new(
        "PRAG0467",
        "A domain action cannot transition after its body",
        "'{0}' declares [TransitionsTo(When = TransitionTiming.AfterBody)], but a domain action builds its response in the body, which would describe the entity before the move. Use BeforeBody, or ByBody if the body performs the transition.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>PRAG0468: <c>[TransitionsTo]</c> on a mutation that does not load the row it would move.</summary>
    public static readonly DiagnosticDescriptor TransitionOnAMutationThatIsNotAnUpdate = new(
        "PRAG0468",
        "[TransitionsTo] needs an Update mutation",
        "'{0}' declares [TransitionsTo] but is not an Update mutation: a Create starts in the initial state, and a Delete or a Restore has a lifecycle of its own. Declare Mode = MutationMode.Update, or remove the attribute.",
        Category, DiagnosticSeverity.Error, true);
}
