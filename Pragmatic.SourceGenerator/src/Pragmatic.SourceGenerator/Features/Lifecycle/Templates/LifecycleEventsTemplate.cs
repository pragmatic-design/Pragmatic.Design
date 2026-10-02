using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Lifecycle.Models;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Templates;

/// <summary>
///     Generates <c>{Entity}.LifecycleEvents.g.cs</c> — a partial of the entity implementing
///     <c>IRaisesLifecycleEvents</c>. At each declared lifecycle transition it raises the mapped events,
///     each constructed from entity members by name. The lifecycle interceptor invokes it during
///     <c>SavingChanges</c>; the existing dispatch publishes the events after commit.
/// </summary>
internal sealed class LifecycleEventsTemplate : CSharpTemplate
{
    private readonly LifecycleEventsModel _model;

    public LifecycleEventsTemplate(LifecycleEventsModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Lifecycle";
    protected override string? TriggerInfo => $"[Raises<...>] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "LifecycleEvents", _model.Namespace),
        ToSourceText());

    // Only emit when the entity can actually raise events (derives from DomainEventSource).
    protected override bool Validate() => _model.IsDomainEventSource && _model.Raises.AsImmutableArray().Length > 0;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();
        XmlSummary($"SG-generated lifecycle-event raising for <see cref=\"{_model.TypeName}\"/>.");
        Class(_model.TypeName, RenderBody,
            baseType: null,
            interfaces: ["global::Pragmatic.Events.IRaisesLifecycleEvents"],
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        // Explicit interface implementation — no access modifier (emitted manually, not via Method()).
        AppendLine("void global::Pragmatic.Events.IRaisesLifecycleEvents.RaiseLifecycleEvents(global::Pragmatic.Events.EntityLifecycle lifecycle)");
        Block(() =>
        {
            foreach (var group in _model.Raises.AsImmutableArray().GroupBy(r => r.Lifecycle))
            {
                AppendLine($"if (lifecycle == global::Pragmatic.Events.EntityLifecycle.{group.Key})");
                Block(() =>
                {
                    foreach (var r in group)
                        AppendLine($"RaiseEvent(new {r.EventType}({string.Join(", ", r.Args.AsImmutableArray())}));");
                });
            }
        });
    }

    private static AccessModifier ParseAccessibility(string accessibility) => accessibility.ToLowerInvariant() switch
    {
        "public" => AccessModifier.Public,
        "internal" => AccessModifier.Internal,
        "protected" => AccessModifier.Protected,
        "private" => AccessModifier.Private,
        _ => AccessModifier.Public
    };
}
