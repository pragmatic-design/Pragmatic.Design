using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>{Saga}.EventHandlers.g.cs</c> — one nested <c>EventHandler_{EventName}</c>
///     per event consumed by the saga, each implementing <c>IMessageHandler&lt;TEvent&gt;</c>
///     and delegating to <c>Orchestrator.HandleEventAsync</c>.
///     Closes the dispatch gap: events published via <c>IMessageBus</c> now automatically
///     advance the saga state machine.
/// </summary>
internal sealed class SagaEventHandlerTemplate : CSharpTemplate
{
    private readonly SagaModel _model;

    public SagaEventHandlerTemplate(SagaModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"[Saga<{_model.StateTypeShortName}>] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "EventHandlers", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => !_model.Steps.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Messaging");
        AddUsing("Pragmatic.Messaging.Saga");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"SG-generated partial for <see cref=\"{_model.TypeName}\"/>. Contains one <c>IMessageHandler&lt;TEvent&gt;</c> per event consumed by the saga.");
        Class(_model.TypeName, RenderOuterBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
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

    private void RenderOuterBody()
    {
        // One nested handler per distinct event type across all steps (including start)
        var emitted = new System.Collections.Generic.HashSet<string>();
        var first = true;
        foreach (var step in _model.Steps)
        {
            if (!emitted.Add(step.EventTypeFqn)) continue;
            if (!first) AppendLine();
            first = false;
            RenderHandlerClass(step);
        }
    }

    private void RenderHandlerClass(SagaStepModel step)
    {
        var className = $"EventHandler_{step.EventTypeShortName}";

        XmlSummary($"Routes <see cref=\"{step.EventTypeFqn}\"/> to <see cref=\"Orchestrator\"/>.");
        Class(className, () =>
        {
            AppendLine("private readonly Orchestrator _orchestrator;");
            AppendLine();

            AppendLine($"public {className}(Orchestrator orchestrator) => _orchestrator = orchestrator;");
            AppendLine();

            // Correlation: ICorrelatedMessage.CorrelationId or the [CorrelationKey] property
            // (accessor resolved at compile time by SagaTransform; PRAG0820 when neither).
            var correlation = step.CorrelationAccessor ?? "CorrelationId";
            AppendLine($"public global::System.Threading.Tasks.Task HandleAsync({step.EventTypeFqn} message, global::Pragmatic.Messaging.MessageContext context, global::System.Threading.CancellationToken ct = default)");
            IncreaseIndent();
            AppendLine($"=> _orchestrator.HandleEventAsync(message, typeof({step.EventTypeFqn}), message.{correlation}, ct);");
            DecreaseIndent();
        },
        interfaces: new System.Collections.Generic.List<string> { $"global::Pragmatic.Messaging.IMessageHandler<{step.EventTypeFqn}>" },
        accessModifier: AccessModifier.Internal,
        modifiers: new ClassModifiers { Sealed = true });
    }
}
