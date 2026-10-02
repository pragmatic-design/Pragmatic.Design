using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates state machine methods on entity partial classes:
///     <c>CanTransitionTo()</c>, <c>TransitionTo()</c>, <c>AllowedTransitions</c>.
///     Uses the generated <c>Set{Property}()</c> setter for change tracking integration.
/// </summary>
internal sealed class StateMachineTemplate : CSharpTemplate
{
    private readonly StateMachineModel _model;

    public StateMachineTemplate(StateMachineModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.EntityTypeName} StateMachine ({_model.EnumFullTypeName})";
    protected override string? TriggerInfo => $"[StateMachine<{_model.EnumFullTypeName}>] on {_model.EntityTypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.EntityTypeName, "StateMachine", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, States.Length: > 0 };
    }

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary(
            $"State machine methods for {_model.EntityTypeName} driven by {_model.EnumFullTypeName}.");

        Class(_model.EntityTypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        RenderCanTransitionTo();
        AppendLine();
        RenderTransitionTo();
        AppendLine();
        RenderAllowedTransitions();
    }

    private void RenderCanTransitionTo()
    {
        var enumType = $"global::{_model.EnumFullTypeName}";

        XmlSummary("Checks whether a transition from the current state to the target state is allowed.");
        XmlParam("targetState", "The desired target state.");
        AppendLine("/// <returns><c>true</c> if the transition is valid; <c>false</c> otherwise.</returns>");

        Method("CanTransitionTo", () =>
        {
            AppendLine($"return ({_model.PropertyName}, targetState) switch");
            AppendLine("{");
            IncreaseIndent();

            foreach (var state in _model.States)
            {
                foreach (var from in state.TransitionFromStates)
                {
                    AppendLine(
                        $"({enumType}.{from}, {enumType}.{state.Name}) => true,");
                }
            }

            AppendLine("_ => false,");
            DecreaseIndent();
            AppendLine("};");
        }, "bool",
            [new MethodParameter(enumType, "targetState")],
            AccessModifier.Public);
    }

    private void RenderTransitionTo()
    {
        var enumType = $"global::{_model.EnumFullTypeName}";
        var setterMethodName = $"Set{_model.PropertyName}";

        XmlSummary("Attempts to transition to the target state. Returns a ConflictError if the transition is invalid.");
        XmlParam("targetState", "The desired target state.");

        Method("TransitionTo", () =>
        {
            // Validate transition
            AppendLine("if (!CanTransitionTo(targetState))");
            Block(() =>
            {
                AppendLine(
                    $"return new global::Pragmatic.Result.Http.ConflictError {{ EntityType = \"{_model.EntityTypeName}\", Reason = $\"Cannot transition from '{{{_model.PropertyName}}}' to '{{targetState}}'.\" }};");
            });
            AppendLine();

            // Call user-defined guard method if present (CanEnter{State}() → bool)
            if (_model.GuardedStates.Count > 0)
            {
                RenderGuardChecks();
                AppendLine();
            }

            // Apply the transition via the generated setter (tracks in _modifiedProperties)
            AppendLine($"{setterMethodName}(targetState);");

            // Raise domain events if entity supports them
            if (_model.HasDomainEvents)
            {
                RenderEventRaising();
            }

            AppendLine();
            AppendLine(
                "return global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success();");
        }, "global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>",
            [new MethodParameter(enumType, "targetState")],
            AccessModifier.Public);
    }

    /// <summary>
    ///     Generates guard method calls for states with user-defined CanEnter{State}() methods.
    /// </summary>
    private void RenderGuardChecks()
    {
        var enumType = $"global::{_model.EnumFullTypeName}";

        Comment("Business rule guards (user-defined CanEnter{State}() methods)");
        foreach (var stateName in _model.GuardedStates.OrderBy(s => s))
        {
            AppendLine($"if (targetState == {enumType}.{stateName} && !CanEnter{stateName}())");
            Block(() =>
            {
                AppendLine(
                    $"return new global::Pragmatic.Result.Http.ConflictError {{ EntityType = \"{_model.EntityTypeName}\", Reason = $\"Business rule prevents entering state '{stateName}'.\" }};");
            });
        }
    }

    private void RenderEventRaising()
    {
        // Check if any state has events
        var statesWithEvents = _model.States.Where(s => s.RaisedEvents.Length > 0).ToList();
        if (statesWithEvents.Count == 0)
            return;

        AppendLine();
        Comment("Raise domain events for this transition");
        Switch("targetState", () =>
        {
            foreach (var state in statesWithEvents)
            {
                Case($"global::{_model.EnumFullTypeName}.{state.Name}", () =>
                {
                    foreach (var raised in state.RaisedEvents)
                    {
                        var args = string.Join(", ", raised.CtorArguments);
                        AppendLine($"RaiseEvent(new global::{raised.TypeName}({args}));");
                    }

                    Break();
                });
            }
        });
    }

    private void RenderAllowedTransitions()
    {
        var enumType = $"global::{_model.EnumFullTypeName}";

        XmlSummary("Gets the states that can be reached from the current state.");

        Method("AllowedTransitions", () =>
        {
            // Group transitions by source state
            var transitionsBySource = new Dictionary<string, List<string>>();

            foreach (var state in _model.States)
            {
                foreach (var from in state.TransitionFromStates)
                {
                    if (!transitionsBySource.TryGetValue(from, out var targets))
                    {
                        targets = new List<string>();
                        transitionsBySource[from] = targets;
                    }

                    targets.Add(state.Name);
                }
            }

            AppendLine($"return {_model.PropertyName} switch");
            AppendLine("{");
            IncreaseIndent();

            foreach (var kvp in transitionsBySource)
            {
                var targets = string.Join(", ",
                    kvp.Value.Select(t => $"{enumType}.{t}"));
                AppendLine($"{enumType}.{kvp.Key} => [{targets}],");
            }

            AppendLine("_ => [],");
            DecreaseIndent();
            AppendLine("};");
        }, $"global::System.ReadOnlySpan<{enumType}>",
            [],
            AccessModifier.Public);
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
