using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Validates state machine graph integrity at compile time.
///     Checks for missing initial state, unreachable states, and invalid transition sources.
/// </summary>
internal static class StateMachineValidator
{
    public static ImmutableArray<Diagnostic> Validate(StateMachineModel model, Location location)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var stateNames = new HashSet<string>(model.States.Select(s => s.Name));

        // PRAG0623: the governed property does not exist. Reported before anything else, because every
        // other finding here is about a machine that cannot be generated at all.
        if (!model.PropertyExists)
        {
            diagnostics.Add(Diagnostic.Create(
                PersistenceDiagnostics.StateMachinePropertyNotFound,
                location,
                model.EnumFullTypeName,
                model.EntityTypeName,
                model.PropertyName));
        }

        // PRAG0620: No initial state defined
        if (model.InitialStateName is null)
        {
            diagnostics.Add(Diagnostic.Create(
                PersistenceDiagnostics.StateMachineMissingInitialState,
                location,
                model.EntityTypeName));
        }

        // PRAG0638: more than one. The transform takes the first and drops the rest, so without this
        // the entry state is whichever value the enum declares first — an order nobody chose.
        var initialStates = model.States.Where(s => s.IsInitial).Select(s => s.Name).ToList();
        if (initialStates.Count > 1)
        {
            diagnostics.Add(Diagnostic.Create(
                PersistenceDiagnostics.StateMachineMultipleInitialStates,
                location,
                model.EntityTypeName,
                initialStates.Count,
                string.Join(", ", initialStates)));
        }

        foreach (var state in model.States)
        {
            // PRAG0622: [TransitionFrom] references non-existent state
            foreach (var fromState in state.TransitionFromStates)
            {
                if (!stateNames.Contains(fromState))
                {
                    diagnostics.Add(Diagnostic.Create(
                        PersistenceDiagnostics.StateMachineInvalidTransitionSource,
                        location,
                        fromState,
                        state.Name,
                        model.EnumFullTypeName));
                }
            }
        }

        // PRAG0621: Unreachable state (no incoming transitions and not initial)
        // Build set of states that are transition targets (have incoming edges)
        // Every state marked initial counts as reachable, not just the one the transform kept: with two
        // of them PRAG0638 already says what is wrong, and PRAG0621 piling "can never be reached" onto
        // the discarded one describes the generator's arbitrary choice rather than the declaration.
        var reachableStates = new HashSet<string>(model.States.Where(s => s.IsInitial).Select(s => s.Name));
        if (model.InitialStateName is not null)
            reachableStates.Add(model.InitialStateName);

        // A state is reachable if any other state has [TransitionFrom] pointing away from it
        // (meaning the state CAN be a target of a transition)
        // TransitionFromStates on state X = "which states can transition TO X"
        // So X itself is a target — it has incoming transitions from those states
        foreach (var state in model.States)
        {
            if (state.TransitionFromStates.Length > 0)
                reachableStates.Add(state.Name);
        }

        foreach (var state in model.States)
        {
            if (!reachableStates.Contains(state.Name))
            {
                diagnostics.Add(Diagnostic.Create(
                    PersistenceDiagnostics.StateMachineUnreachableState,
                    location,
                    state.Name,
                    model.EntityTypeName));
            }
        }

        return diagnostics.ToImmutable();
    }
}
