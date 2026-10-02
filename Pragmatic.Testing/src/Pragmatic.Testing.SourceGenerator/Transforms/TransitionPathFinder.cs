using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     The transitions a contract walks to bring an entity from its initial state to a state from which a
///     transition is legal — or illegal — when the initial state is not one.
/// </summary>
/// <remarks>
///     <para>
///         The half of each state-transition contract that does not start from the initial state needs a
///         multi-step flow, and the flow is in the declarations — every
///         transition endpoint of the entity names its target, and the target's <c>[TransitionFrom]</c> set
///         names where it may be entered from — so it is found, not written.
///     </para>
///     <para>
///         Breadth first, so the walk is the shortest one: each step is a real request with its own body
///         and permission, and a longer walk is more that can fail for a reason that is not the contract.
///     </para>
/// </remarks>
internal static class TransitionPathFinder
{
    /// <summary>The shortest walk to a state <paramref name="target" /> may be entered from, or <c>null</c>.</summary>
    public static IReadOnlyList<StateTransitionModel>? ToALegalSource(
        StateTransitionModel target, IReadOnlyList<StateTransitionModel> ofTheEntity)
        => Shortest(target, ofTheEntity, state => IsLegalSource(target, state));

    /// <summary>The shortest walk to a state <paramref name="target" /> may not be entered from, or <c>null</c>.</summary>
    public static IReadOnlyList<StateTransitionModel>? ToAnIllegalSource(
        StateTransitionModel target, IReadOnlyList<StateTransitionModel> ofTheEntity)
        => Shortest(target, ofTheEntity, state => !IsLegalSource(target, state));

    private static bool IsLegalSource(StateTransitionModel target, string state)
        => target.LegalSources.Any(s => string.Equals(s, state, StringComparison.Ordinal));

    private static IReadOnlyList<StateTransitionModel>? Shortest(
        StateTransitionModel target,
        IReadOnlyList<StateTransitionModel> ofTheEntity,
        Func<string, bool> goal)
    {
        var start = target.InitialState;
        var cameFrom = new Dictionary<string, (string From, StateTransitionModel Via)>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var queue = new Queue<string>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();

            foreach (var step in ofTheEntity)
            {
                if (!IsLegalSource(step, state) || !seen.Add(step.TargetState))
                    continue;

                cameFrom[step.TargetState] = (state, step);
                if (goal(step.TargetState))
                    return Walk(step.TargetState, start, cameFrom);

                queue.Enqueue(step.TargetState);
            }
        }

        return null;
    }

    private static IReadOnlyList<StateTransitionModel> Walk(
        string end, string start, Dictionary<string, (string From, StateTransitionModel Via)> cameFrom)
    {
        var steps = new List<StateTransitionModel>();
        for (var state = end; state != start; state = cameFrom[state].From)
            steps.Add(cameFrom[state].Via);

        steps.Reverse();
        return steps;
    }
}
