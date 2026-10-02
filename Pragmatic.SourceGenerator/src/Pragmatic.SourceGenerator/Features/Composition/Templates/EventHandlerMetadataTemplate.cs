using System;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.EventHandlers, ...)] attribute.
///     Enables host projects to discover and call AddPragmaticEventHandlers().
/// </summary>
internal sealed class EventHandlerMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EventHandlerModel> _handlers;
    private readonly bool _indent;
    private readonly string _namespacePrefix;

    public EventHandlerMetadataTemplate(
        string namespacePrefix,
        ImmutableArray<EventHandlerModel> handlers,
        bool indent)
    {
        _namespacePrefix = namespacePrefix;
        _handlers = handlers;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new(
        "_Metadata.EventHandlers.g.cs",
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.EventHandlers, \"{MetadataSchemaVersions.EventHandlers}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Composition.SourceGenerator");
        builder.Property("registrationMethod",
            $"{_namespacePrefix}.EventHandlerRegistrationExtensions.AddPragmaticEventHandlers");

        builder.Property("data");
        builder.StartObject();
        // One entry per handler-event pair, and the count matches the array: a class may handle
        // several events, and a count that did not agree with what follows it is the kind
        // of disagreement nobody notices until something reads both.
        var pairs = _handlers.SelectMany(h => h.EventTypeFullNames.Select(e => (Handler: h, Event: e)))
            .OrderBy(p => p.Handler.FullTypeName, StringComparer.Ordinal)
            .ThenBy(p => p.Event, StringComparer.Ordinal)
            .ToList();

        builder.Property("handlersCount", pairs.Count);

        if (_indent && pairs.Count > 0)
        {
            builder.Property("handlers");
            builder.StartArray();
            foreach (var pair in pairs)
            {
                builder.StartObject();
                builder.Property("type", pair.Handler.FullTypeName);
                builder.Property("event", pair.Event);
                builder.EndObject();
            }

            builder.EndArray();
        }

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }
}
