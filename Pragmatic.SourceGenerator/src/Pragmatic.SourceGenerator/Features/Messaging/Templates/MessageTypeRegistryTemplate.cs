using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.TypeRegistry.g.cs</c> — AOT-safe switch expression
///     that deserializes outbox messages by FQN without reflection.
/// </summary>
internal sealed class MessageTypeRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageTypeModel> _messageTypes;

    public MessageTypeRegistryTemplate(ImmutableArray<MessageTypeModel> messageTypes)
        => _messageTypes = messageTypes;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_messageTypes.Length} message type(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "TypeRegistry"),
        ToSourceText());

    protected override bool Validate() => !_messageTypes.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("System.Text.Json");
        AddUsing("System.Text.Json.Serialization.Metadata");
        AddUsing("Pragmatic.Messaging.Entities");
        AppendLine();

        AppendLine("namespace Pragmatic.Messaging.Generated;");
        AppendLine();

        XmlSummary("AOT-safe message type registry. Deserializes outbox messages via compile-time switch expression.");
        Class("PragmaticMessageTypeRegistry", RenderBody,
            baseType: null,
            interfaces: ["global::Pragmatic.Messaging.Entities.IMessageTypeRegistry"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        // Options come from the shared PragmaticJsonOptions seam so that host-registered
        // source-generated contexts (and the AOT fallback opt-out) apply to outbox deserialization.
        AppendLine("private readonly JsonSerializerOptions _options;");
        AppendLine();
        AppendLine("public PragmaticMessageTypeRegistry(global::Pragmatic.Serialization.PragmaticJsonOptions? jsonOptions = null)");
        IncreaseIndent();
        AppendLine("=> _options = (jsonOptions ?? global::Pragmatic.Serialization.PragmaticJsonOptions.Default).Build();");
        DecreaseIndent();
        AppendLine();

        XmlInheritDoc();
        Method("Deserialize", RenderDeserializeBody,
            "object?",
            [
                new MethodParameter { Type = "string", Name = "fullyQualifiedTypeName" },
                new MethodParameter { Type = "string", Name = "json" },
            ]);
    }

    private void RenderDeserializeBody()
    {
        AppendLine("return fullyQualifiedTypeName switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var messageType in _messageTypes)
        {
            // Use global:: prefix on the type argument to avoid ambiguity when assemblies share simple names.
            var qualifiedFqn = messageType.Fqn.StartsWith("global::", StringComparison.Ordinal)
                ? messageType.Fqn
                : $"global::{messageType.Fqn}";

            // The case label must match the runtime lookup key, which is Type.FullName
            // (stored by OutboxInterceptor) — that has NO "global::" prefix. Strip it so
            // the switch actually matches instead of always falling through to null.
            var runtimeKey = messageType.Fqn.StartsWith("global::", StringComparison.Ordinal)
                ? messageType.Fqn.Substring("global::".Length)
                : messageType.Fqn;

            // AOT-clean: resolve the JsonTypeInfo from the seam (generated context covers this type) and
            // use the JsonTypeInfo-based overload, which carries no IL2026/IL3050. Falls back to the
            // options' reflection resolver only when reflection fallback is left enabled.
            AppendLine($"\"{runtimeKey}\" => JsonSerializer.Deserialize(json, (JsonTypeInfo<{qualifiedFqn}>)_options.GetTypeInfo(typeof({qualifiedFqn}))),");
        }

        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }
}
