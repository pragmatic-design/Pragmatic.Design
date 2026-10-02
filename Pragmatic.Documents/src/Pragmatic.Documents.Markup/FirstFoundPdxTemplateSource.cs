namespace Pragmatic.Documents.Markup;

/// <summary>
///     The first of several sources that has the template — an organisation's own before the
///     application's.
/// </summary>
/// <remarks>
///     Asked piece by piece: a partial is looked up here too, so an organisation that overrides only its
///     letterhead keeps the application's letter, and one that overrides only the letter keeps the
///     application's letterhead.
/// </remarks>
public sealed class FirstFoundPdxTemplateSource : IPdxTemplateSource
{
    private readonly IPdxTemplateSource[] _sources;

    /// <param name="sources">The sources, in the order they are asked.</param>
    public FirstFoundPdxTemplateSource(params IPdxTemplateSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
            throw new ArgumentException("At least one source is needed.", nameof(sources));

        _sources = sources;
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindAsync(string name, CancellationToken ct = default)
    {
        foreach (var source in _sources)
            if (await source.FindAsync(name, ct).ConfigureAwait(false) is { } markup)
                return markup;

        return null;
    }

    /// <inheritdoc />
    public override string ToString() => string.Join(" → ", _sources.Select(source => source.ToString()));
}
