using System.Text.Json;

namespace Pragmatic.SourceGenerator.Features.I18n.Transforms;

/// <summary>
///     JSON parsing methods for TranslationKeysTransform.
/// </summary>
internal static partial class TranslationKeysTransform
{
    /// <summary>
    ///     Parses flat JSON object into key-value pairs.
    /// </summary>
    private static Dictionary<string, string> ParseJsonToKeys(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        FlattenJsonElement(doc.RootElement, "", result);

        return result;
    }

    private static void FlattenJsonElement(
        JsonElement element,
        string prefix,
        Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : $"{prefix}.{prop.Name}";
                    FlattenJsonElement(prop.Value, key, result);
                }

                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? "";
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.ToString();
                break;
        }
    }
}
