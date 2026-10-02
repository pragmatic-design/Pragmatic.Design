using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.DispatchTable.g.cs</c> — a typed dispatch table that
///     pattern-matches messages to strongly-typed <c>PublishAsync</c> calls,
///     eliminating <c>MakeGenericMethod</c> reflection at runtime.
/// </summary>
internal sealed class MessageDispatchTableTemplate : CSharpTemplate
{
    private readonly ImmutableArray<string> _messageTypeFqns;
    private readonly string _assemblyNamespace;

    public MessageDispatchTableTemplate(
        ImmutableArray<string> messageTypeFqns,
        string assemblyNamespace)
    {
        _messageTypeFqns = messageTypeFqns;
        _assemblyNamespace = assemblyNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? SourceInfo => "MessageDispatchTable";
    protected override string? TriggerInfo => $"{_messageTypeFqns.Length} message type(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "DispatchTable"),
        ToSourceText());

    protected override bool Validate() => !_messageTypeFqns.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AppendLine();

        AppendLine($"namespace {_assemblyNamespace}.Generated;");
        AppendLine();

        XmlSummary(
            "AOT-safe message dispatch table. Routes messages to strongly-typed " +
            "<c>PublishAsync</c> calls via compile-time pattern matching.");
        Class("GeneratedMessageDispatchTable", RenderBody,
            interfaces: ["global::Pragmatic.Messaging.ITypedMessageDispatchTable"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlInheritDoc();
        AppendLine("public Task? TryDispatch(global::Pragmatic.Messaging.IMessageBus bus, object message, global::Pragmatic.Messaging.MessageContext context, CancellationToken ct)");
        IncreaseIndent();
        AppendLine("=> message switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var fqn in _messageTypeFqns)
        {
            // MessageTypeFqn already carries the global:: prefix (same convention as
            // HandlerRegistrationTemplate's generic arguments).
            AppendLine($"{fqn} typed => bus.PublishAsync(typed, context, ct),");
        }

        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
    }
}
