using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     PdxTemplates metadata reader — the assemblies that embed document and mail templates.
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     The anchor types <c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c> named, one per assembly, in a
    ///     stable order.
    /// </summary>
    public static EquatableArray<string> ExtractPdxTemplateAnchors(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var anchors = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in assemblies.SelectMany(a => a.Entries))
        {
            if (entry.Category != MetadataCategoryIds.PdxTemplates || string.IsNullOrEmpty(entry.JsonData))
                continue;

            using var document = JsonDocument.Parse(entry.JsonData);
            if (document.RootElement.TryGetProperty("anchor", out var anchor)
                && anchor.GetString() is { Length: > 0 } type)
                anchors.Add(type);
        }

        return anchors.ToImmutableArray();
    }
}
