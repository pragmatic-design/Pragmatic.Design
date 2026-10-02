using System.Reflection;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     The sources <c>AddPdxTemplates</c> registers, asked in the order they are added.
/// </summary>
public sealed class PdxTemplateSourcesBuilder
{
    private readonly List<IPdxTemplateSource> _sources;

    internal PdxTemplateSourcesBuilder(List<IPdxTemplateSource> sources) => _sources = sources;

    /// <summary>The templates embedded in the assembly that declares <typeparamref name="TMarker" />.</summary>
    public PdxTemplateSourcesBuilder FromAssemblyOf<TMarker>() => From(new EmbeddedPdxTemplateSource(typeof(TMarker).Assembly));

    /// <summary>The templates embedded in <paramref name="assembly" />.</summary>
    public PdxTemplateSourcesBuilder FromAssembly(Assembly assembly) => From(new EmbeddedPdxTemplateSource(assembly));

    /// <summary>The templates in <paramref name="directory" />.</summary>
    public PdxTemplateSourcesBuilder FromDirectory(string directory) => From(new DirectoryPdxTemplateSource(directory));

    /// <summary>A source of your own.</summary>
    public PdxTemplateSourcesBuilder From(IPdxTemplateSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }
}
