using System.Text.Json;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     The <c>references.json</c> beside a directory of reference artefacts: for every file, where it comes
///     from (<c>origin</c>), under what terms (<c>licence</c>), what produced it (<c>producedBy</c>, a tool and
///     its version, never the code the reference checks) and when (<c>date</c>).
///     <c>scripts/visual-references.py</c> writes both the files and their entries.
/// </summary>
public static class ReferenceManifest
{
    public const string FileName = "references.json";

    private static readonly string[] Fields = ["origin", "licence", "producedBy", "date"];

    /// <summary>Fails, naming each one, on a file with no entry, an entry with no file, or an entry missing a field.</summary>
    public static void AssertComplete(string directory)
    {
        var problems = Problems(directory);
        if (problems.Count > 0)
            throw new VisualMismatchException($"{Path.Combine(directory, FileName)} does not account for its references:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", problems)}");
    }

    public static IReadOnlyList<string> Problems(string directory)
    {
        var manifestPath = Path.Combine(directory, FileName);
        if (!File.Exists(manifestPath))
            return [$"there is no {FileName} in {directory}"];

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifest.RootElement.TryGetProperty("references", out var references) || references.ValueKind != JsonValueKind.Object)
            return [$"{FileName} has no \"references\" object"];

        var problems = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in references.EnumerateObject())
        {
            listed.Add(entry.Name);
            if (!File.Exists(Path.Combine(directory, entry.Name)))
                problems.Add($"{entry.Name}: listed, but there is no such file");
            foreach (var field in Fields)
            {
                if (!entry.Value.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    problems.Add($"{entry.Name}: no \"{field}\"");
            }
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            if (file != FileName && !listed.Contains(file!))
                problems.Add($"{file}: a file with no entry");
        }

        return problems;
    }
}
