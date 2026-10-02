using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Templates;

/// <summary>
///     Renders a declared tag/key template as the C# literal that produces it, expanding
///     <c>{Property}</c> placeholders against a receiver.
/// </summary>
/// <remarks>
///     The receiver differs by consumer: the <c>ICacheInvalidator</c> partial reads the declaring
///     instance (<c>this</c>), the generated domain-event handler reads the dispatched event
///     (<c>@event</c>) because it is a separate object.
/// </remarks>
internal static class CachePlaceholderExpander
{
    public static string Expand(
        string template,
        EquatableArray<PlaceholderPropertyModel> properties,
        string receiver)
    {
        if (!template.Contains("{"))
            return $"\"{StringHelper.CSharpLiteral(template)}\"";

        var result = template;
        foreach (var prop in properties)
            result = result.Replace($"{{{prop.Name}}}", $"{{{receiver}.{prop.Name}}}");

        // Escape backslash/quote in the literal portions so an author-supplied " or \ in a tag/key
        // cannot break out of the interpolated string. The {…} holes carry no " or \.
        result = result.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return $"$\"{result}\"";
    }
}
