using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Emits <c>_Infra.Messaging.SagaDiagrams.g.cs</c> with <c>PragmaticSagaDiagrams.{Saga}</c>:
///     one Mermaid <c>stateDiagram-v2</c> per saga as a compile-time constant — the state graph
///     the orchestrator enforces, rendered from the same model. Surface it in docs or a
///     diagnostics endpoint.
/// </summary>
internal sealed class SagaDiagramTemplate : CSharpTemplate
{
    private readonly ImmutableArray<SagaModel> _sagas;

    public SagaDiagramTemplate(ImmutableArray<SagaModel> sagas) => _sagas = sagas;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_sagas.Length} saga(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "SagaDiagrams"),
        ToSourceText());

    protected override bool Validate() => !_sagas.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Messaging.Generated;");
        AppendLine();
        XmlSummary("Generated Mermaid state diagrams of this assembly's sagas.");
        Class("PragmaticSagaDiagrams", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderBody()
    {
        var first = true;
        foreach (var saga in _sagas.OrderBy(s => s.TypeName, System.StringComparer.Ordinal))
        {
            if (!first)
                AppendLine();
            first = false;

            XmlSummary($"State machine of {saga.TypeName} (state: {saga.StateTypeShortName}) as a Mermaid stateDiagram-v2.");
            AppendLine($"public const string {saga.TypeName} = @\"{BuildMermaid(saga)}\";");
        }
    }

    private static string BuildMermaid(SagaModel saga)
    {
        var lines = new List<string> { "stateDiagram-v2" };

        if (saga.StartStep is { } start)
        {
            var entry = start.NextState ?? saga.StateValues.FirstOrDefault() ?? "Started";
            lines.Add($"    [*] --> {entry}: {start.EventTypeShortName}");
            AppendStepExtras(lines, start, entry);
        }

        foreach (var step in saga.Steps.Where(s => !s.IsStart))
        {
            foreach (var validState in step.ValidStates)
            {
                // No NextState = the handler observes the event without moving the machine.
                var target = step.NextState ?? validState;
                lines.Add($"    {validState} --> {target}: {step.EventTypeShortName}");
                AppendStepExtras(lines, step, validState);
            }
        }

        // A state that is entered but never awaited in any [InState] is terminal.
        var awaited = new HashSet<string>(saga.Steps.SelectMany(s => s.ValidStates));
        var entered = saga.Steps
            .Where(s => s.NextState is not null)
            .Select(s => s.NextState!)
            .Distinct()
            .Where(s => !awaited.Contains(s));
        foreach (var terminal in entered.OrderBy(s => s, System.StringComparer.Ordinal))
            lines.Add($"    {terminal} --> [*]");

        return string.Join("\n", lines).Replace("\"", "\"\"");
    }

    private static void AppendStepExtras(List<string> lines, SagaStepModel step, string state)
    {
        if (step.TimeoutDuration is not null)
            lines.Add($"    {state} --> [*]: onTimeout({step.TimeoutDuration})");

        if (step.CompensationActionFqn is not null)
        {
            var action = step.CompensationActionFqn.Substring(step.CompensationActionFqn.LastIndexOf('.') + 1);
            lines.Add($"    note right of {state}: compensates with {action}");
        }
    }
}
