namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>Why a <c>[TransitionsTo]</c> cannot be performed as declared; each has its own diagnostic.</summary>
internal enum TransitionProblem
{
    None = 0,

    /// <summary>No entity the operation writes has <c>[StateMachine&lt;TState&gt;]</c> of that enum (PRAG0465).</summary>
    NoEntityWithThatStateMachine,

    /// <summary>More than one loaded entity has it, and the generator will not guess (PRAG0465).</summary>
    AmbiguousEntity,

    /// <summary>The body also calls <c>TransitionTo(target)</c> while the invoker performs it (PRAG0466).</summary>
    BodyAlsoTransitions,

    /// <summary><c>AfterBody</c> on a domain action, whose response the body has already built (PRAG0467).</summary>
    AfterBodyOnAnAction,

    /// <summary>On a mutation that does not load the row it would move (PRAG0468).</summary>
    MutationIsNotAnUpdate
}
