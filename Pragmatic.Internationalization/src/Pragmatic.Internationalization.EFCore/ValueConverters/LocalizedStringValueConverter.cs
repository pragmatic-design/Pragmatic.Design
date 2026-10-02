using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Internationalization.Types;
using Pragmatic.Serialization;

namespace Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="LocalizedString" /> to/from a JSON column.
///     Translations are persisted as a JSON object of culture → value
///     (e.g. <c>{"en":"Widget","it":"Componente"}</c>).
/// </summary>
/// <remarks>
///     <see cref="LocalizedString" /> is a reference type, so a single converter covers both
///     nullable and non-nullable properties: EF Core never invokes a value converter for
///     <see langword="null" /> values (null in → null out).
/// </remarks>
public sealed class LocalizedStringValueConverter : ValueConverter<LocalizedString, string>
{
    /// <summary>
    ///     Creates a new instance of the converter.
    /// </summary>
    public LocalizedStringValueConverter()
        : base(
            localized => Serialize(localized),
            json => Deserialize(json))
    {
    }

    private static string Serialize(LocalizedString value)
        => JsonSerializer.Serialize(ToDictionary(value), PragmaticCommonJsonContext.Default.DictionaryStringString);

    private static LocalizedString Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new LocalizedString();

        try
        {
            var map = JsonSerializer.Deserialize(json, PragmaticCommonJsonContext.Default.DictionaryStringString);
            return LocalizedString.From(map ?? new Dictionary<string, string>());
        }
        catch (JsonException ex)
        {
            // A corrupted/truncated JSON column would otherwise fail the whole query with an
            // opaque JsonException. Include a snippet of the offending value to locate the bad row.
            var snippet = json.Length > 64 ? json[..64] + "…" : json;
            throw new InvalidOperationException(
                $"Failed to deserialize a LocalizedString column: the stored JSON is invalid ('{snippet}'). " +
                $"The row's data may be corrupted or truncated.", ex);
        }
    }

    private static Dictionary<string, string> ToDictionary(LocalizedString value)
    {
        var dictionary = new Dictionary<string, string>(value.Count);
        foreach (var translation in value)
            dictionary[translation.Key] = translation.Value;
        return dictionary;
    }
}
