using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Glossary.Models;

namespace Pragmatic.SourceGenerator.Features.Glossary.Templates;

/// <summary>
///     Emits <c>_Infra.AsyncApi.Generated.g.cs</c> with <c>PragmaticAsyncApi.Json</c>: an AsyncAPI 3.0
///     document cataloguing the domain events (one channel + message per event type) as a compile-time
///     constant — serve it like the generated OpenAPI. "Events are the async API."
/// </summary>
internal sealed class AsyncApiTemplate : CSharpTemplate
{
    private readonly EquatableArray<AsyncApiEventModel> _events;
    private readonly string _namespace;
    private readonly string _title;

    public AsyncApiTemplate(EquatableArray<AsyncApiEventModel> events, string assemblyName)
    {
        _events = events;
        _namespace = string.IsNullOrEmpty(assemblyName) ? "Pragmatic.Generated" : $"{assemblyName}.Generated";
        _title = string.IsNullOrEmpty(assemblyName) ? "Pragmatic events" : $"{assemblyName} events";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/AsyncApi";

    protected override bool Validate() => !_events.IsDefaultOrEmpty;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("AsyncApi", "Generated"), ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        XmlSummary("Generated AsyncAPI 3.0 document for the domain events (the async API).");
        AppendLine("public static class PragmaticAsyncApi");
        AppendLine("{");
        IncreaseIndent();
        XmlSummary("The AsyncAPI 3.0 document as JSON.");
        AppendLine($"public const string Json = @\"{BuildJson()}\";");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The channel/message key for each event, keyed by its full name. Two events are the same event
    ///     only when their FULL names match — deduplicating on the simple name silently drops one of two
    ///     homonymous events from different boundaries. When a simple name is shared, both keys are
    ///     qualified; an unambiguous name stays short, which is what the document is read with.
    /// </summary>
    /// <summary>
    ///     The channel and message key of each event: always its fully-qualified name.
    /// </summary>
    /// <remarks>
    ///     Qualifying only on collision would keep today's keys shorter, and make them unstable: adding
    ///     <c>Billing.StatusChanged</c> tomorrow would rename the existing <c>StatusChanged</c> channel
    ///     to <c>Booking.StatusChanged</c> — a published contract broken by an addition somewhere else
    ///     entirely. The name is what consumers bind to, so it is worth more than its length.
    ///     <c>VirtualFolderHints</c> qualifies unconditionally for the same reason.
    /// </remarks>
    private static Dictionary<string, string> BuildKeys(IReadOnlyList<AsyncApiEventModel> events)
        => events.ToDictionary(e => e.FullName, e => e.FullName, StringComparer.Ordinal);

    private string BuildJson()
    {
        var events = _events.GroupBy(e => e.FullName, StringComparer.Ordinal).Select(g => g.First()).ToList();
        var keys = BuildKeys(events);
        events = [.. events.OrderBy(e => keys[e.FullName], StringComparer.Ordinal)];

        var b = new MetadataJsonBuilder(indent: true);
        b.StartObject();
        b.Property("asyncapi").Value("3.0.0");
        b.Property("info").StartObject();
        b.Property("title").Value(_title);
        b.Property("version").Value("1.0.0");
        b.EndObject();

        // The rule the addresses below came from, named rather than implied: this document is written at
        // compile time and the router is a runtime service, so a reader with a custom IMessageRouter can
        // see which convention was assumed. See MessageTopicConvention.
        b.Property("x-pragmatic-address-rule").Value(MessageTopicConvention.Rule);

        b.Property("channels").StartObject();
        foreach (var e in events)
        {
            var key = keys[e.FullName];
            b.Property(key).StartObject();
            // ⚠️ The topic the transport carries it on, NOT the type name. The key above is the
            // document's identifier and stays fully qualified; the address is what a consumer
            // configures, and every event of one boundary legitimately shares it. With the type's full
            // name as the address too, a consumer that read the contract and subscribed to the channel
            // it named would subscribe to nothing.
            b.Property("address").Value(MessageTopicConvention.TopicFor(e.Namespace));
            b.Property("messages").StartObject();
            b.Property(key).StartObject();
            b.Property("$ref").Value($"#/components/messages/{key}");
            b.EndObject();
            b.EndObject();
            b.EndObject();
        }
        b.EndObject();

        b.Property("components").StartObject();
        b.Property("messages").StartObject();
        foreach (var e in events)
        {
            var key = keys[e.FullName];
            b.Property(key).StartObject();
            b.Property("name").Value(key);
            // Two-level event model: distinguish the public integration contract from internal domain events.
            b.Property("x-pragmatic-public", e.IsPublic);
            if (e.IsObsolete)
                b.Property("x-pragmatic-obsolete", true);
            b.Property("payload").StartObject();
            b.Property("type").Value("object");
            if (!e.Properties.IsDefaultOrEmpty)
            {
                // Concrete payload schema — makes the AsyncAPI a real, snapshot-testable contract.
                b.Property("properties").StartObject();
                foreach (var p in e.Properties)
                {
                    b.Property(p.Name).StartObject();
                    b.Property("type").Value(p.JsonType);
                    b.EndObject();
                }
                b.EndObject();
            }
            b.EndObject();
            b.EndObject();
        }
        b.EndObject();
        b.EndObject();

        b.EndObject();

        // Escape for a C# verbatim string literal (@"…"): double the quotes; newlines are preserved.
        return b.ToString().Replace("\"", "\"\"");
    }
}
