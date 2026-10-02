using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.Topology.g.cs</c> — transport topology declarations
///     (exchanges, queues, bindings) derived from handler models.
/// </summary>
internal sealed class TopologyTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageHandlerModel> _handlers;

    public TopologyTemplate(ImmutableArray<MessageHandlerModel> handlers)
        => _handlers = handlers;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_handlers.Length} handler(s) topology";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "Topology"),
        ToSourceText());

    protected override bool Validate() => !_handlers.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Messaging.Generated;");
        AppendLine();

        XmlSummary("SG-generated transport topology: exchanges, queues, and bindings for this assembly's message handlers.");
        Class("PragmaticMessagingTopology", RenderBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        // Collect unique exchanges (by message type namespace → boundary)
        var exchanges = _handlers
            .Select(h => ExtractExchange(h.MessageTypeFqn))
            .Distinct()
            .OrderBy(e => e)
            .ToList();

        // Emit exchange constants
        XmlSummary("Exchanges declared for message boundaries.");
        Class("Exchanges", () =>
        {
            foreach (var exchange in exchanges)
            {
                AppendLine($"public const string {SanitizeName(exchange)} = \"{exchange}\";");
            }
        }, accessModifier: AccessModifier.Internal, modifiers: new ClassModifiers { IsStatic = true });

        AppendLine();

        // Emit queue constants
        XmlSummary("Queues declared for message handler subscriptions.");
        Class("Queues", () =>
        {
            foreach (var handler in _handlers)
            {
                var queueName = ExtractQueue(handler);
                var constName = SanitizeName(queueName);
                AppendLine($"public const string {constName} = \"{queueName}\";");
            }
        }, accessModifier: AccessModifier.Internal, modifiers: new ClassModifiers { IsStatic = true });

        AppendLine();

        // Emit DeclareTopology method
        XmlSummary("Declares all exchanges, queues, and bindings for this assembly.");
        Method("GetBindings", () =>
        {
            AppendLine("return new global::Pragmatic.Messaging.Routing.TopologyBinding[]");
            AppendLine("{");
            IncreaseIndent();

            for (var i = 0; i < _handlers.Length; i++)
            {
                var handler = _handlers[i];
                var exchange = ExtractExchange(handler.MessageTypeFqn);
                var queue = ExtractQueue(handler);
                var routingKey = handler.MessageTypeShortName;
                var comma = i < _handlers.Length - 1 ? "," : "";

                AppendLine($"new(\"{exchange}\", \"{queue}\", \"{routingKey}\"){comma}");
            }

            DecreaseIndent();
            AppendLine("};");
        },
        "global::Pragmatic.Messaging.Routing.TopologyBinding[]",
        modifiers: new MethodModifiers { IsStatic = true });
    }

    private static string ExtractExchange(string messageTypeFqn)
    {
        // "global::Showcase.Booking.Events.ReservationCreated" → "booking.events"
        // parts: ["Showcase", "Booking", "Events", "ReservationCreated"]
        // parts.Length - 3 = index 1 = "Booking"
        var fqn = messageTypeFqn.Replace("global::", "");
        var parts = fqn.Split('.');
        if (parts.Length >= 3)
            return $"{ToKebabCase(parts[parts.Length - 3])}.events";
        if (parts.Length >= 2)
            return $"{ToKebabCase(parts[0])}.events";
        return "default.events";
    }

    private static string ExtractQueue(MessageHandlerModel handler)
    {
        // Handler namespace → boundary, handler name → queue
        var nsParts = handler.Namespace.Split('.');
        var boundary = nsParts.Length >= 2 ? ToKebabCase(nsParts[1]) : ToKebabCase(nsParts[0]);
        var handlerName = ToKebabCase(handler.TypeName);
        return $"{boundary}.{handlerName}";
    }

    private static string SanitizeName(string name)
    {
        return name.Replace('.', '_').Replace('-', '_');
    }

    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
