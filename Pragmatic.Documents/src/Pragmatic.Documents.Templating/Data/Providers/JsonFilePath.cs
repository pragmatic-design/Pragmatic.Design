namespace Pragmatic.Documents.Templating.Data.Providers;

/// <summary>
/// Path handling shared by the JSON file data sources.
/// </summary>
/// <remarks>
/// Trust boundary: the file path originates from the application developer wiring up the
/// data source, not from untrusted template content. Paths are nonetheless canonicalized to
/// an absolute form, and an optional <c>allowedRoot</c> can confine reads to a directory tree,
/// rejecting traversal (<c>..</c>) outside it.
/// </remarks>
internal static class JsonFilePath
{
    /// <summary>Canonicalize <paramref name="filePath"/> and, if <paramref name="allowedRoot"/> is set, reject traversal outside it.</summary>
    public static string Normalize(string filePath, string? allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);

        if (allowedRoot is not null)
        {
            var fullRoot = Path.GetFullPath(allowedRoot);
            var rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootWithSep, StringComparison.Ordinal)
                && !string.Equals(fullPath, fullRoot, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Resolved path '{fullPath}' escapes the allowed root '{fullRoot}'.");
            }
        }

        return fullPath;
    }

    /// <summary>Open the file for reading, throwing a clear error when it is missing.</summary>
    public static FileStream OpenRead(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Data source file not found: '{filePath}'.", filePath);
        return File.OpenRead(filePath);
    }
}
