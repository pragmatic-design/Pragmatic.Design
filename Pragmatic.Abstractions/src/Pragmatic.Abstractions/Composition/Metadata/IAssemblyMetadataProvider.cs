namespace Pragmatic.Composition.Metadata;

/// <summary>
///     Provides assembly-level Pragmatic metadata without using reflection.
///     Implemented by SG-generated classes in each module assembly.
/// </summary>
/// <remarks>
///     <para>
///         The source generator emits both <c>[assembly: PragmaticMetadata(...)]</c> attributes
///         and an implementation of this interface. Runtime consumers should prefer this interface
///         over reading assembly attributes via reflection.
///     </para>
/// </remarks>
public interface IAssemblyMetadataProvider
{
    /// <summary>
    ///     Gets all metadata entries for this assembly, keyed by <see cref="MetadataCategory"/>.
    /// </summary>
    IReadOnlyList<AssemblyMetadataEntry> GetMetadata();

    /// <summary>
    ///     Gets only the metadata entries belonging to the specified <paramref name="category"/>.
    /// </summary>
    /// <param name="category">The metadata category to filter by.</param>
    /// <returns>
    ///     The subset of <see cref="GetMetadata()"/> whose
    ///     <see cref="AssemblyMetadataEntry.Category"/> equals <paramref name="category"/>.
    /// </returns>
    /// <remarks>
    ///     Default implementation filters the full list returned by <see cref="GetMetadata()"/>.
    ///     Generated providers may override this with a precomputed lookup for efficiency.
    /// </remarks>
    IReadOnlyList<AssemblyMetadataEntry> GetMetadata(MetadataCategory category)
    {
        var all = GetMetadata();
        var result = new List<AssemblyMetadataEntry>(all.Count);
        foreach (var entry in all)
            if (entry.Category == category)
                result.Add(entry);
        return result;
    }
}

/// <summary>
///     A single metadata entry equivalent to a <c>[assembly: PragmaticMetadata(...)]</c> attribute.
/// </summary>
/// <param name="Category">The metadata category.</param>
/// <param name="SchemaVersion">The schema version.</param>
/// <param name="JsonData">The JSON metadata payload.</param>
public readonly record struct AssemblyMetadataEntry(
    MetadataCategory Category,
    string SchemaVersion,
    string JsonData);
