namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
/// Path canonicalization and traversal-rejection for file-backed spreadsheet data sources.
/// </summary>
/// <remarks>
/// Trust boundary: file paths originate from template configuration. We canonicalize to an
/// absolute path (collapsing any relative <c>..</c> segments) so the resolved target is explicit
/// and stable, and — when an <c>allowedRoot</c> is supplied — reject any path that escapes that
/// root. A bare <c>..</c> in an otherwise-legitimate relative path is NOT rejected (that is normal
/// path navigation); the security boundary is "must stay under allowedRoot", enforced post-canonicalization.
/// This is a defence-in-depth guard, not a sandbox.
/// </remarks>
internal static class SpreadsheetPath
{
    public static string Validate(string filePath, string? allowedRoot = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

        // Canonicalize first: GetFullPath resolves and collapses any relative ".." segments, so a
        // legitimate relative path (e.g. "bin/../output/x.csv") is handled, while traversal that
        // would escape a configured root is caught by the post-canonicalization check below.
        var fullPath = Path.GetFullPath(filePath);

        if (!string.IsNullOrWhiteSpace(allowedRoot))
        {
            var rootFull = Path.GetFullPath(allowedRoot);
            var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, rootFull, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"File path '{fullPath}' escapes the allowed root '{rootFull}'.");
        }

        return fullPath;
    }
}
