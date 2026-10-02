using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Templates;

/// <summary>
/// Generates [assembly: PragmaticMetadata(MetadataCategory.Caching, ...)]
/// when Pragmatic.Composition is referenced.
/// </summary>
internal sealed class CachingMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<CacheableModel> _cacheables;
    private readonly ImmutableArray<InvalidatesModel> _invalidators;
    private readonly bool _indent;

    public CachingMetadataTemplate(
        ImmutableArray<CacheableModel> cacheables,
        ImmutableArray<InvalidatesModel> invalidators,
        bool indent)
    {
        _cacheables = cacheables;
        _invalidators = invalidators;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Caching";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Caching"),
        ToSourceText());

    protected override bool Validate()
        => !_cacheables.IsDefaultOrEmpty || !_invalidators.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.Caching, \"{MetadataSchemaVersions.Caching}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    /// <summary>
    ///     The metadata document, also handed straight to the host when the declaring assembly *is* the
    ///     host — see <c>CachingFeature.LocalCachingRegistrations</c>. Rendering it from here rather
    ///     than rebuilding it there is what keeps the two paths from drifting.
    /// </summary>
    public string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.SourceGenerator/Caching");
        builder.PropertyNull("registrationMethod");

        builder.Property("data");
        builder.StartObject();

        builder.Property("cacheablesCount", _cacheables.IsDefaultOrEmpty ? 0 : _cacheables.Length);

        if (_indent && !_cacheables.IsDefaultOrEmpty)
        {
            builder.Property("cacheables");
            builder.StartArray();
            foreach (var c in _cacheables.OrderBy(c => c.TypeName))
            {
                builder.StartObject();
                builder.Property("type", $"global::{(string.IsNullOrEmpty(c.Namespace) ? c.TypeName : $"{c.Namespace}.{c.TypeName}")}");
                builder.Property("duration", c.Duration);
                if (!c.Tags.IsDefaultOrEmpty)
                    builder.PropertyArray("tags", c.Tags);
                builder.EndObject();
            }
            builder.EndArray();
        }

        // Collect unique cache categories across all cacheables + invalidators
        var categories = new HashSet<string>();
        if (!_cacheables.IsDefaultOrEmpty)
            foreach (var c in _cacheables)
                if (c.CategoryTypeFqn is not null) categories.Add(c.CategoryTypeFqn);
        if (!_invalidators.IsDefaultOrEmpty)
            foreach (var inv in _invalidators)
                if (inv.CategoryTypeFqn is not null) categories.Add(inv.CategoryTypeFqn);

        if (categories.Count > 0)
            builder.PropertyArray("categories", categories.OrderBy(c => c).ToArray());

        builder.Property("invalidatorsCount", _invalidators.IsDefaultOrEmpty ? 0 : _invalidators.Length);

        if (_indent && !_invalidators.IsDefaultOrEmpty)
        {
            builder.Property("invalidators");
            builder.StartArray();
            foreach (var inv in _invalidators.OrderBy(i => i.TypeName))
            {
                builder.StartObject();
                builder.Property("type", $"global::{(string.IsNullOrEmpty(inv.Namespace) ? inv.TypeName : $"{inv.Namespace}.{inv.TypeName}")}");
                if (!inv.Tags.IsDefaultOrEmpty)
                    builder.PropertyArray("tags", inv.Tags);
                if (!inv.Keys.IsDefaultOrEmpty)
                    builder.PropertyArray("keys", inv.Keys);
                builder.EndObject();
            }
            builder.EndArray();
        }

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }

}
