using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Transforms;

/// <summary>
///     Aggregation methods for TranslationKeysTransform.
/// </summary>
internal static partial class TranslationKeysTransform
{
    /// <summary>
    ///     Aggregates all translations by key, building the Translations dictionary.
    /// </summary>
    private static Dictionary<string, TranslationKeyModel> AggregateByKey(
        ImmutableArray<(string Path, string Content)> files,
        FolderStructure structure,
        TranslationKeysConfiguration config)
    {
        var result = new Dictionary<string, TranslationKeyModel>(StringComparer.Ordinal);

        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var culture = ExtractCulture(path, structure);
            var baseName = ExtractBaseName(path, structure);

            Dictionary<string, string> keys;
            try
            {
                keys = ParseJsonToKeys(content);
            }
            catch
            {
                continue;
            }

            foreach (var kvp in keys)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                // Build full key: for ByFile mode, prepend base name
                var fullKey = config.ByFile && !string.IsNullOrEmpty(baseName) && structure != FolderStructure.Simple
                    ? $"{baseName}.{key}"
                    : key;

                if (!result.TryGetValue(fullKey, out var existing))
                {
                    var segments = fullKey.Split('.');
                    var propertyName = ToPascalCase(segments[segments.Length - 1]);
                    var pathSegments = segments.Length > 1
                        ? segments.Take(segments.Length - 1).Select(ToPascalCase).ToImmutableArray()
                        : ImmutableArray<string>.Empty;

                    existing = new TranslationKeyModel(
                        fullKey,
                        propertyName,
                        pathSegments,
                        culture == config.DefaultCulture ? value : null,
                        ImmutableDictionary<string, string>.Empty.Add(culture, value));

                    result[fullKey] = existing;
                }
                else
                {
                    // Add this culture's translation
                    var newTranslations = existing.Translations.ContainsKey(culture)
                        ? existing.Translations
                        : existing.Translations.Add(culture, value);

                    var sampleValue = existing.SampleValue;
                    if (culture == config.DefaultCulture && string.IsNullOrEmpty(sampleValue))
                        sampleValue = value;

                    result[fullKey] = new TranslationKeyModel(
                        existing.FullKey,
                        existing.PropertyName,
                        existing.PathSegments,
                        sampleValue,
                        newTranslations);
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     Extracts culture code from file path.
    /// </summary>
    private static string ExtractCulture(string path, FolderStructure structure)
    {
        var normalized = NormalizePath(path);

        return structure switch
        {
            FolderStructure.FolderPerCulture => Path.GetFileName(Path.GetDirectoryName(normalized)) ?? "en",
            FolderStructure.FlatWithSuffix => Path.GetFileNameWithoutExtension(normalized).Split('.').Last(),
            FolderStructure.Simple => Path.GetFileNameWithoutExtension(normalized),
            _ => "en"
        };
    }

    /// <summary>
    ///     Extracts base file name (without culture) for grouping.
    /// </summary>
    private static string ExtractBaseName(string path, FolderStructure structure)
    {
        var normalized = NormalizePath(path);
        var fileName = Path.GetFileNameWithoutExtension(normalized);

        return structure switch
        {
            FolderStructure.FolderPerCulture => fileName,
            FolderStructure.FlatWithSuffix =>
                string.Join(".", fileName.Split('.').Take(fileName.Split('.').Length - 1)),
            FolderStructure.Simple => "", // No base name for simple structure
            _ => ""
        };
    }

    /// <summary>
    ///     Gets unique file base names for ByFile grouping.
    /// </summary>
    private static ImmutableArray<string> GetFileNames(
        ImmutableArray<(string Path, string Content)> files,
        FolderStructure structure)
    {
        return files
            .Select(f => ExtractBaseName(f.Path, structure))
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToImmutableArray();
    }
}
