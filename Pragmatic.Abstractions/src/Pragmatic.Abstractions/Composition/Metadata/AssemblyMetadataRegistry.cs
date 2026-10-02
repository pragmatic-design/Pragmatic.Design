using System.Collections.Immutable;

namespace Pragmatic.Composition.Metadata;

/// <summary>
///     Static registry for assembly metadata providers.
///     SG-generated module initializers register providers here at startup.
///     Runtime consumers query this registry instead of using reflection to read assembly attributes.
/// </summary>
public static class AssemblyMetadataRegistry
{
    private static ImmutableArray<IAssemblyMetadataProvider> _providers = [];

    /// <summary>
    ///     Registers a metadata provider. Called by SG-generated code at startup.
    /// </summary>
    /// <param name="provider">The metadata provider to register.</param>
    public static void Register(IAssemblyMetadataProvider provider)
        => ImmutableInterlocked.Update(ref _providers, static (list, p) => list.Add(p), provider);

    /// <summary>
    ///     Gets all registered metadata providers.
    /// </summary>
    public static IReadOnlyList<IAssemblyMetadataProvider> GetProviders()
        => _providers;

    /// <summary>
    ///     Gets all metadata entries matching the specified category from all registered providers.
    /// </summary>
    /// <param name="category">The metadata category to filter by.</param>
    /// <returns>All matching metadata entries.</returns>
    public static IEnumerable<AssemblyMetadataEntry> GetByCategory(MetadataCategory category)
    {
        foreach (var provider in _providers)
        foreach (var entry in provider.GetMetadata())
            if (entry.Category == category)
                yield return entry;
    }

    /// <summary>
    ///     Gets the first metadata entry matching the specified category, or null if none.
    /// </summary>
    /// <param name="category">The metadata category to find.</param>
    /// <returns>The first matching entry, or null.</returns>
    public static AssemblyMetadataEntry? FindByCategory(MetadataCategory category)
    {
        foreach (var provider in _providers)
        foreach (var entry in provider.GetMetadata())
            if (entry.Category == category)
                return entry;

        return null;
    }
}
