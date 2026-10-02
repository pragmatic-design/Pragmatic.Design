using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.Routing.g.cs</c> — compile-time IMessageRouter
///     with switch expression mapping FQN → topic/queue.
/// </summary>
internal sealed class RoutingRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageHandlerModel> _handlers;

    public RoutingRegistryTemplate(ImmutableArray<MessageHandlerModel> handlers)
        => _handlers = handlers;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_handlers.Length} handler(s) routing";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "Routing"),
        ToSourceText());

    protected override bool Validate() => !_handlers.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Messaging.Routing");
        AppendLine();

        AppendLine("namespace Pragmatic.Messaging.Generated;");
        AppendLine();

        XmlSummary("SG-generated message router. Maps message types to topics and handlers to queues at compile-time.");
        Class("PragmaticMessageRouter", RenderBody,
            baseType: null,
            interfaces: ["global::Pragmatic.Messaging.Routing.IMessageRouter"],
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        // GetTopic<T>() — generic method, rendered manually
        XmlInheritDoc();
        AppendLine("public string GetTopic<T>() where T : notnull => GetTopic(typeof(T));");

        AppendLine();

        // GetTopic(Type)
        XmlInheritDoc();
        Method("GetTopic", RenderGetTopicBody,
            "string",
            new System.Collections.Generic.List<MethodParameter>
            {
                new() { Type = "global::System.Type", Name = "messageType" }
            });

        AppendLine();

        // GetSendQueue(Type) — override the interface default (send.{FullName}) so p2p queue names
        // follow the same {boundary}.commands.{message} convention as DefaultMessageRouter. Without
        // this the in-memory (DefaultMessageRouter) and transport (this) routers disagree on names.
        XmlInheritDoc();
        Method("GetSendQueue", RenderGetSendQueueBody,
            "string",
            new System.Collections.Generic.List<MethodParameter>
            {
                new() { Type = "global::System.Type", Name = "messageType" }
            });

        AppendLine();
        RenderFallbackHelpers();
    }

    private void RenderGetSendQueueBody()
    {
        AppendLine("var fqn = messageType.FullName;");
        AppendLine("return $\"{ExtractBoundary(fqn ?? \"default\")}.commands.{ToKebabCase(messageType.Name)}\";");
    }

    private void RenderGetTopicBody()
    {
        // Deduplicate message types → topics
        var topicMap = _handlers
            .Select(h => (Fqn: StripGlobal(h.MessageTypeFqn), Topic: ExtractExchange(h.MessageTypeFqn)))
            .GroupBy(x => x.Fqn)
            .Select(g => g.First())
            .OrderBy(x => x.Fqn)
            .ToList();

        AppendLine("var fqn = messageType.FullName;");
        AppendLine("return fqn switch");
        AppendLine("{");
        IncreaseIndent();

        foreach (var item in topicMap)
            AppendLine($"\"{item.Fqn}\" => \"{item.Topic}\",");

        AppendLine($"_ => $\"{{ExtractBoundary(fqn ?? \"default\")}}.events\"");
        DecreaseIndent();
        AppendLine("};");
    }

    // Fallback helpers as class members, because GetTopic's and GetSendQueue's default arms both use
    // them — as local functions of one method they were out of scope in the other (CS0103, latent until
    // the first consumer with a transport reference actually compiled this file).
    private void RenderFallbackHelpers()
    {
        AppendLine("private static string ExtractBoundary(string ns)");
        Block(() =>
        {
            AppendLine("var parts = ns.Split('.');");
            AppendLine("return parts.Length >= 2 ? ToKebabCase(parts[1]) : ToKebabCase(parts[0]);");
        });

        AppendLine();
        AppendLine("private static string ToKebabCase(string name)");
        Block(() =>
        {
            AppendLine("if (string.IsNullOrEmpty(name)) return name;");
            AppendLine("var sb = new global::System.Text.StringBuilder();");
            AppendLine("for (var i = 0; i < name.Length; i++)");
            Block(() =>
            {
                AppendLine("if (char.IsUpper(name[i]) && i > 0) sb.Append('-');");
                AppendLine("sb.Append(char.ToLowerInvariant(name[i]));");
            });
            AppendLine("return sb.ToString();");
        });
    }

    // Reuse the same extraction logic as TopologyTemplate
    private static string ExtractExchange(string messageTypeFqn)
    {
        var fqn = StripGlobal(messageTypeFqn);
        var parts = fqn.Split('.');
        if (parts.Length >= 3)
            return $"{ToKebabCase(parts[parts.Length - 3])}.events";
        return "default.events";
    }

    private static string StripGlobal(string fqn) => fqn.Replace("global::", "");

    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
