namespace Pragmatic.Documents.Markup;

/// <summary>
///     Templates read from a directory, by path relative to it.
/// </summary>
/// <remarks>
///     For templates an operator edits in place. ⚠️ A name that resolves outside the directory —
///     <c>"../appsettings.json"</c> — is refused, whatever is there: a template name can come from data
///     (a tenant setting, a request), and this is what keeps it from reading any file the process can.
/// </remarks>
public sealed class DirectoryPdxTemplateSource : IPdxTemplateSource
{
    private readonly string _root;

    /// <param name="directory">The directory the names are relative to.</param>
    public DirectoryPdxTemplateSource(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _root = Path.GetFullPath(directory);
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var path = Path.GetFullPath(Path.Combine(_root, name));
        var inside = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;

        if (!path.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{name}' resolves outside {_root}.", nameof(name));

        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(DirectoryPdxTemplateSource)}({_root})";
}
