using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads the parts of a roles.pragmatic.json for <see cref="RoleSeedingTransform" />, reporting each one it
///     cannot as <c>PRAG1011</c> with the file's path.
/// </summary>
internal sealed class RoleSeedingReader(string path, Action<Diagnostic> report)
{
    /// <summary>Where a diagnostic about the file points: the position when it is known, the file's start otherwise.</summary>
    public static Location At(string path, int line, int column)
    {
        var position = new LinePosition(line, column);
        return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(position, position));
    }

    public void Problem(string problem)
        => report(Diagnostic.Create(IdentityDiagnostics.RoleSeedingFileInvalid, At(path, 0, 0), path, problem));

    public void UnknownProperties(JsonElement element, string[] known, string what)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
                Problem($"'{property.Name}' is not a property of {what} — it has {string.Join(", ", known.Select(k => $"'{k}'"))}");
        }
    }

    public string? OptionalString(JsonElement element, string name, string owner)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        Problem($"'{name}' of {owner} must be a string");
        return null;
    }

    public ImmutableArray<string> Strings(JsonElement element, string name, string owner)
    {
        if (!element.TryGetProperty(name, out var value))
            return ImmutableArray<string>.Empty;

        if (value.ValueKind != JsonValueKind.Array)
        {
            Problem($"'{name}' of {owner} must be an array of strings — none of it is seeded");
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                builder.Add(text);
            else
                Problem($"'{name}' of {owner} holds {item.GetRawText()}, which is not a non-empty string");
        }

        return builder.ToImmutable();
    }
}
