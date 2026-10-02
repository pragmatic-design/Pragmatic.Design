namespace Pragmatic.Migrations.Cli.Snapshot;

/// <summary>Writes one schema snapshot file — only when its content changes.</summary>
/// <remarks>
///     <para>
///         The build regenerates every snapshot after every build, and most of the time the schema is the same.
///         Rewriting identical content bought nothing and cost a random build failure: a process holding a mapped
///         view of the file — a git client hashing the working tree, an indexer, reacting to the rewrite the previous
///         build had just made — makes a truncating write fail on Windows with <c>ERROR_USER_MAPPED_FILE</c>.
///         Reading shares the file with such a reader; only a real change writes it.
///     </para>
///     <para>
///         ⚠️ A snapshot that <b>does</b> change while something maps it still fails, and says so: that is a real
///         schema change, rare, and the message names the file.
///     </para>
/// </remarks>
public static class SchemaSnapshotWriter
{
    /// <summary>Writes <paramref name="content" /> to <paramref name="path" /> unless the file already holds it.</summary>
    /// <returns>Whether the file was written.</returns>
    public static async Task<bool> WriteAsync(string path, string content)
    {
        if (File.Exists(path)
            && string.Equals(await File.ReadAllTextAsync(path).ConfigureAwait(false), content, StringComparison.Ordinal))
            return false;

        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        return true;
    }
}
