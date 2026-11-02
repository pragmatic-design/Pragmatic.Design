using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     The stored form of <see cref="LocalizedString" />: a JSON object of culture → text (<c>{"en":"Widget","it":"Componente"}</c>), the one
///     every persistence writes. EF Core's <c>LocalizedStringValueConverter</c> and the Sql and documents engines read and write it through these
///     two methods, so one database holds the same text whichever wrote it.
/// </summary>
public sealed partial class LocalizedString
{
    /// <summary>The translations as the JSON object a column or a document field holds.</summary>
    public string ToJson()
    {
        var dictionary = new Dictionary<string, string>(Count);
        foreach (var translation in this)
            dictionary[translation.Key] = translation.Value;
        return JsonSerializer.Serialize(dictionary, PragmaticCommonJsonContext.Default.DictionaryStringString);
    }

    /// <summary>The localized string a stored JSON object holds; empty for a null or empty text.</summary>
    /// <exception cref="InvalidOperationException">The text is not a JSON object of culture → text: the row's data is corrupted or truncated.</exception>
    public static LocalizedString FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new LocalizedString();

        try
        {
            var map = JsonSerializer.Deserialize(json, PragmaticCommonJsonContext.Default.DictionaryStringString);
            return From(map ?? new Dictionary<string, string>());
        }
        catch (JsonException ex)
        {
            // A corrupted or truncated value would otherwise fail the whole query with an opaque JsonException: a snippet locates the row.
            var snippet = json.Length > 64 ? json[..64] + "…" : json;
            throw new InvalidOperationException(
                $"Failed to deserialize a LocalizedString column: the stored JSON is invalid ('{snippet}'). " +
                $"The row's data may be corrupted or truncated.", ex);
        }
    }
}
