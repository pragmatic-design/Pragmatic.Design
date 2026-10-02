// Pragmatic.SourceGenerator - Composition - Startup Metadata Template

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates [assembly: PragmaticMetadata(MetadataCategory.Startup, ...)] attribute.
/// </summary>
internal sealed class StartupMetadataTemplate : CSharpTemplate
{
    private readonly bool _indent;
    private readonly string _namespacePrefix;
    private readonly ImmutableArray<StartupModel> _startups;

    public StartupMetadataTemplate(
        string namespacePrefix,
        ImmutableArray<StartupModel> startups,
        bool indent)
    {
        _namespacePrefix = namespacePrefix;
        _startups = startups;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new(
        "_Metadata.Startup.g.cs",
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        // Build the JSON
        var json = BuildJson();
        var ns = string.IsNullOrEmpty(_namespacePrefix) ? "Pragmatic" : _namespacePrefix;
        var registrationMethod = $"{ns}.PragmaticDependencies.AddPipelineSteps";

        // Generate the assembly attribute
        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Startup, \"{MetadataSchemaVersions.Startup}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    private string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Composition.SourceGenerator");

        var ns = string.IsNullOrEmpty(_namespacePrefix) ? "Pragmatic" : _namespacePrefix;
        builder.Property("registrationMethod", $"{ns}.PragmaticDependencies.AddPipelineSteps");

        builder.Property("data");
        builder.StartObject();

        builder.Property("modules");
        builder.StartArray();

        foreach (var startup in _startups.OrderBy(s => s.Priority).ThenBy(s => s.FullTypeName))
        {
            builder.StartObject();
            builder.Property("type", startup.FullTypeName);
            builder.Property("priority", startup.Priority);
            if (!startup.RequiredConfigSections.IsDefaultOrEmpty)
                builder.PropertyArray("requiredConfigs", startup.RequiredConfigSections);
            builder.EndObject();
        }

        builder.EndArray();
        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }
}
