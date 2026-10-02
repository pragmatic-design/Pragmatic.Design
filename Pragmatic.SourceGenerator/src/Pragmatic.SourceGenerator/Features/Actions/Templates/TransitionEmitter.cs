using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The lines of the override that performs or checks a <c>[TransitionsTo]</c>, shared by the mutation
///     and the domain-action invoker so the two say the same thing.
/// </summary>
internal static class TransitionEmitter
{
    /// <summary>The body of <c>TransitionBeforeBody</c>/<c>TransitionAfterBody</c>: move, and answer the refusal.</summary>
    /// <remarks>
    ///     <c>entity</c> is the expression that reaches the row: <c>entity</c> in a mutation invoker,
    ///     <c>action._invoice</c> in an action's.
    /// </remarks>
    public static IEnumerable<string> PerformLines(TransitionModel transition, string entity)
    {
        yield return $"var __moved = {entity}.TransitionTo({transition.TargetExpression});";
        yield return "return __moved.IsFailure ? __moved.Error : null;";
    }

    /// <summary>The body of <c>EnsureTheBodyTransitioned</c>: a declaration the body did not honour throws.</summary>
    public static IEnumerable<string> CheckLines(TransitionModel transition, string entity, string operationName)
    {
        var state = $"{entity}.{transition.StatePropertyName}";
        yield return $"if ({state} != {transition.TargetExpression})";
        yield return "    throw new global::System.InvalidOperationException(";
        yield return $"        \"{operationName} declares [TransitionsTo] {transition.TargetMember} with When = ByBody, \" +";
        yield return $"        $\"and its body succeeded leaving {transition.StatePropertyName} at '{{{state}}}'. \" +";
        yield return "        \"Move the entity in the body, or declare IsConditional = true if it moves only sometimes.\");";
    }
}
