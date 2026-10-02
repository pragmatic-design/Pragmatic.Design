using System.Text.Json;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     JSON-parsing concern of <see cref="JsonLocalizationProvider"/>: turns translation
///     documents into the in-memory <c>CultureData</c> (simple strings + plural forms).
/// </summary>
public sealed partial class JsonLocalizationProvider
{
    private static CultureData ParseJson(string json)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var plurals = new Dictionary<string, PluralString>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        ParseElement(doc.RootElement, "", strings, plurals);

        return new CultureData(strings, plurals);
    }

    private static void ParseElement(
        JsonElement element,
        string prefix,
        Dictionary<string, string> strings,
        Dictionary<string, PluralString> plurals)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    strings[key] = property.Value.GetString() ?? "";
                    break;

                case JsonValueKind.Object:
                    // Check if this is a plural definition
                    if (IsPluralDefinition(property.Value))
                        plurals[key] = ParsePluralForms(property.Value);
                    else
                        // Nested object - recurse
                        ParseElement(property.Value, key, strings, plurals);
                    break;
            }
        }
    }

    private static bool IsPluralDefinition(JsonElement element)
    {
        // A plural definition has at least one of the CLDR plural categories
        var pluralKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "zero", "one", "two", "few", "many", "other"
        };

        foreach (var property in element.EnumerateObject())
            if (pluralKeys.Contains(property.Name))
                return true;

        return false;
    }

    private static PluralString ParsePluralForms(JsonElement element)
    {
        var forms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
                forms[property.Name] = property.Value.GetString() ?? "";

        return PluralString.FromDictionary(forms);
    }
}
