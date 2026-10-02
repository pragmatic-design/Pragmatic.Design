using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Caching metadata reader — extracts cache categories from [PragmaticMetadata(Caching, ...)] entries.
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts all unique cache category FQNs from Caching metadata across referenced assemblies.
    /// </summary>
    public static ImmutableArray<string> ExtractCacheCategories(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var categories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.Caching = 13. ParseMetadataAttribute stores the enum's ordinal, so
                // the name never matched and no category was ever discovered.
                if (entry.Category != MetadataCategoryIds.Caching || string.IsNullOrEmpty(entry.JsonData))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("data", out var data))
                        continue;

                    if (!data.TryGetProperty("categories", out var cats))
                        continue;

                    foreach (var cat in cats.EnumerateArray())
                    {
                        var value = cat.GetString();
                        if (value is not null)
                            categories.Add(value);
                    }
                }
                catch
                {
                    // Graceful: skip invalid JSON
                }
            }
        }

        return categories.OrderBy(c => c).ToImmutableArray();
    }
}
