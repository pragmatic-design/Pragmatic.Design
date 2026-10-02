namespace Pragmatic.Configuration.Discovery;

/// <summary>
///     The catalogue the generated per-assembly contributions accumulate into.
/// </summary>
/// <remarks>
///     Registered as a single instance and appended to at composition time, the same shape
///     <c>AddRequiredConfiguration</c> uses: repeated contributions add rather than replace, so an
///     assembly discovered later does not erase the ones before it.
/// </remarks>
public sealed class ConfigurationCatalog : IConfigurationCatalog
{
    private readonly List<ConfigurationSectionDescriptor> _sections = [];

    /// <inheritdoc />
    public IReadOnlyList<ConfigurationSectionDescriptor> Sections => _sections;

    /// <summary>
    ///     Adds the sections one assembly declares. Called by generated code; a section already present
    ///     for the same type is ignored, so composing an assembly twice does not duplicate it.
    /// </summary>
    public void Contribute(IEnumerable<ConfigurationSectionDescriptor> sections)
    {
        foreach (var section in sections)
        {
            if (!_sections.Any(s => string.Equals(s.TypeName, section.TypeName, StringComparison.Ordinal)))
                _sections.Add(section);
        }
    }
}
