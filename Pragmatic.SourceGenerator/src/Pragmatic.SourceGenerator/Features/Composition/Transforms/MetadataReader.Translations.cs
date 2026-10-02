using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Translations metadata reader — the languages the referenced modules are translated into.
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     The union of the modules' translation cultures, and the default they agree on.
    /// </summary>
    /// <remarks>
    ///     A default is kept only when it is decidable: every module that names one names the same, and each
    ///     has a translation file for it. Otherwise it is <c>null</c>, and a host that configures none
    ///     refuses to start — the choice is the application's, not a guess made for it.
    /// </remarks>
    public static DeclaredLanguagesModel ExtractDeclaredLanguages(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var cultures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var undecidable = false;

        foreach (var entry in assemblies.SelectMany(a => a.Entries))
        {
            if (entry.Category != MetadataCategoryIds.Translations || string.IsNullOrEmpty(entry.JsonData))
                continue;

            using var document = JsonDocument.Parse(entry.JsonData);
            var root = document.RootElement;

            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("cultures", out var declared) && declared.ValueKind == JsonValueKind.Array)
                foreach (var culture in declared.EnumerateArray())
                    if (culture.GetString() is { Length: > 0 } code)
                        own.Add(code);

            cultures.UnionWith(own);

            if (root.TryGetProperty("defaultCulture", out var @default) && @default.GetString() is { Length: > 0 } defaultCode
                && own.Contains(defaultCode))
                defaults.Add(defaultCode);
            else
                undecidable = true;
        }

        if (cultures.Count == 0)
            return DeclaredLanguagesModel.None;

        return new DeclaredLanguagesModel(
            cultures.ToImmutableArray(),
            !undecidable && defaults.Count == 1 ? defaults.First() : null);
    }

    /// <summary>
    ///     The localization providers the referenced modules generated over their embedded translations,
    ///     in a stable order.
    /// </summary>
    public static EquatableArray<string> ExtractTranslationProviders(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var providers = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in assemblies.SelectMany(a => a.Entries))
        {
            if (entry.Category != MetadataCategoryIds.Translations || string.IsNullOrEmpty(entry.JsonData))
                continue;

            using var document = JsonDocument.Parse(entry.JsonData);
            if (document.RootElement.TryGetProperty("provider", out var provider)
                && provider.GetString() is { Length: > 0 } type)
                providers.Add(type);
        }

        return providers.ToImmutableArray();
    }
}
