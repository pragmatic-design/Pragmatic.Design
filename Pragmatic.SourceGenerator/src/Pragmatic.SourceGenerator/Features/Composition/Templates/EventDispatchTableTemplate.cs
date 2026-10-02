using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates <c>_Infra.Events.DispatchTable.g.cs</c> — a typed dispatch table that
///     pattern-matches domain events to strongly-typed <c>DispatchAsync</c> calls,
///     eliminating <c>MakeGenericMethod</c> reflection at runtime.
/// </summary>
internal sealed class EventDispatchTableTemplate : CSharpTemplate
{
    private readonly ImmutableArray<string> _eventTypeFqns;
    private readonly string _assemblyNamespace;

    public EventDispatchTableTemplate(
        ImmutableArray<string> eventTypeFqns,
        string assemblyNamespace)
    {
        _eventTypeFqns = eventTypeFqns;
        _assemblyNamespace = assemblyNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";
    protected override string? SourceInfo => "EventDispatchTable";
    protected override string? TriggerInfo => $"{_eventTypeFqns.Length} event type(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Events", "DispatchTable"),
        ToSourceText());

    protected override bool Validate() => !_eventTypeFqns.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AppendLine();

        AppendLine($"namespace {_assemblyNamespace}.Generated;");
        AppendLine();

        XmlSummary(
            "AOT-safe event dispatch table. Routes domain events to strongly-typed " +
            "<c>DispatchAsync</c> calls via compile-time pattern matching.");
        Class("GeneratedEventDispatchTable", RenderBody,
            interfaces: ["global::Pragmatic.Events.ITypedEventDispatchTable"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlInheritDoc();
        AppendLine("public Task? TryDispatch(global::Pragmatic.Events.IDomainEventDispatcher dispatcher, global::Pragmatic.Events.IDomainEvent @event, CancellationToken ct)");
        IncreaseIndent();
        AppendLine("=> @event switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var fqn in _eventTypeFqns)
        {
            // fqn already includes global:: from ToDisplayString(FullyQualifiedFormat); do not add another prefix.
            var qualifiedFqn = fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn : $"global::{fqn}";
            AppendLine($"{qualifiedFqn} typed => dispatcher.DispatchAsync(typed, ct),");
        }

        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
    }
}
