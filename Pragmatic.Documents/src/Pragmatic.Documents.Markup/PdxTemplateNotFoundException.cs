namespace Pragmatic.Documents.Markup;

/// <summary>
///     No source had the template. A deployment fault — a file not embedded, a name misspelt — not an
///     outcome of the domain, which is why it is an exception and not a result.
/// </summary>
public sealed class PdxTemplateNotFoundException : Exception
{
    /// <param name="name">The template asked for.</param>
    /// <param name="source">Where it was looked for.</param>
    public PdxTemplateNotFoundException(string name, IPdxTemplateSource source)
        : base($"No template '{name}' in {source}. An embedded template needs an "
               + "<EmbeddedResource Include=\"templates\\**\\*.pdxdoc;templates\\**\\*.pdxemail\" /> in its project.")
    {
        TemplateName = name;
    }

    /// <summary>The template asked for.</summary>
    public string TemplateName { get; }
}
