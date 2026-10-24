using System.Globalization;
using System.Text.Json;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The values as Cosmos DB's JSON holds them: numbers as numbers (Cosmos keeps them as doubles: a decimal is exact up to 15 significant
///     digits, a long up to 2^53), a Guid as its text, an instant as UTC ISO 8601 with seven fraction digits (one length: the strings order as the
///     instants), an enum as its Int32. Used by the generated JSON readers and writers and by the query parameters.
/// </summary>
public static class DocJson
{
    public const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public static void Write(Utf8JsonWriter w, string name, string? value)
    {
        w.WritePropertyName(name);
        WriteValue(w, value);
    }

    public static void Write(Utf8JsonWriter w, string name, int value) => w.WriteNumber(name, value);

    public static void Write(Utf8JsonWriter w, string name, long value) => w.WriteNumber(name, value);

    public static void Write(Utf8JsonWriter w, string name, double value) => w.WriteNumber(name, value);

    public static void Write(Utf8JsonWriter w, string name, decimal value) => w.WriteNumber(name, value);

    public static void Write(Utf8JsonWriter w, string name, bool value) => w.WriteBoolean(name, value);

    public static void Write(Utf8JsonWriter w, string name, Guid value) => w.WriteString(name, value.ToString("D"));

    public static void Write(Utf8JsonWriter w, string name, DateTimeOffset value) => w.WriteString(name, Instant(value));

    public static void Write(Utf8JsonWriter w, string name, DateTime value) => w.WriteString(name, Instant(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))));

    public static void Write<TValue>(Utf8JsonWriter w, string name, TValue? value) where TValue : struct
    {
        w.WritePropertyName(name);
        if (value is { } v)
            WriteBoxed(w, v);
        else
            w.WriteNullValue();
    }

    public static void WriteValue(Utf8JsonWriter w, string? value)
    {
        if (value is null)
            w.WriteNullValue();
        else
            w.WriteStringValue(value);
    }

    public static void WriteValue(Utf8JsonWriter w, int value) => w.WriteNumberValue(value);

    public static void WriteValue(Utf8JsonWriter w, long value) => w.WriteNumberValue(value);

    public static void WriteValue(Utf8JsonWriter w, double value) => w.WriteNumberValue(value);

    public static void WriteValue(Utf8JsonWriter w, decimal value) => w.WriteNumberValue(value);

    public static void WriteValue(Utf8JsonWriter w, bool value) => w.WriteBooleanValue(value);

    public static void WriteValue(Utf8JsonWriter w, Guid value) => w.WriteStringValue(value.ToString("D"));

    public static void WriteValue(Utf8JsonWriter w, DateTimeOffset value) => w.WriteStringValue(Instant(value));

    public static void WriteValue(Utf8JsonWriter w, DateTime value) => w.WriteStringValue(Instant(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))));

    public static void WriteValue<TValue>(Utf8JsonWriter w, TValue? value) where TValue : struct
    {
        if (value is { } v)
            WriteBoxed(w, v);
        else
            w.WriteNullValue();
    }

    /// <summary>The text of a Contains/StartsWith/EndsWith: LINQ throws for a null argument, so does the query.</summary>
    public static void WriteText(Utf8JsonWriter w, string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        w.WriteStringValue(value);
    }

    /// <summary>A list of values as a JSON array (the operand of ARRAY_CONTAINS).</summary>
    public static void WriteArray<TValue>(Utf8JsonWriter w, IEnumerable<TValue> values, Action<Utf8JsonWriter, TValue> write)
    {
        ArgumentNullException.ThrowIfNull(values);
        w.WriteStartArray();
        foreach (var value in values)
            write(w, value);
        w.WriteEndArray();
    }

    public static void WriteList<TValue>(Utf8JsonWriter w, string name, List<TValue>? values, Action<Utf8JsonWriter, TValue> write)
    {
        w.WritePropertyName(name);
        if (values is null)
            w.WriteNullValue();
        else
            WriteArray(w, values, write);
    }

    public static void WriteEmbedded<T>(Utf8JsonWriter w, string name, T? value) where T : IEmbeddedDocument<T>
    {
        w.WritePropertyName(name);
        if (value is null)
        {
            w.WriteNullValue();
            return;
        }

        w.WriteStartObject();
        T.WriteJsonFields(w, value);
        w.WriteEndObject();
    }

    public static void WriteEmbeddedList<T>(Utf8JsonWriter w, string name, List<T>? values) where T : IEmbeddedDocument<T>
    {
        w.WritePropertyName(name);
        if (values is null)
        {
            w.WriteNullValue();
            return;
        }

        w.WriteStartArray();
        foreach (var value in values)
        {
            w.WriteStartObject();
            T.WriteJsonFields(w, value);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    public static T ReadEmbedded<T>(JsonElement document) where T : IEmbeddedDocument<T> => T.ReadJson(document);

    public static List<T> ReadEmbeddedList<T>(JsonElement array) where T : IEmbeddedDocument<T>
    {
        var list = new List<T>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
            list.Add(T.ReadJson(item));
        return list;
    }

    public static List<TValue> ReadList<TValue>(JsonElement array, Func<JsonElement, TValue> read)
    {
        var list = new List<TValue>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Null ? default! : read(item));
        return list;
    }

    public static Guid ReadGuid(JsonElement e) => Guid.Parse(e.GetString()!);

    public static DateTimeOffset ReadInstant(JsonElement e) => DateTimeOffset.ParseExact(e.GetString()!, InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static DateTime ReadDateTime(JsonElement e) => ReadInstant(e).UtcDateTime;

    /// <summary>A number as a decimal from its JSON text (exact as written, up to what Cosmos kept: 15 significant digits).</summary>
    public static decimal ReadDecimal(JsonElement e) => e.GetDecimal();

    public static string Instant(DateTimeOffset value) => value.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    private static void WriteBoxed<TValue>(Utf8JsonWriter w, TValue value) where TValue : struct
    {
        switch (value)
        {
            case int number: w.WriteNumberValue(number); break;
            case long number: w.WriteNumberValue(number); break;
            case double number: w.WriteNumberValue(number); break;
            case decimal number: w.WriteNumberValue(number); break;
            case bool flag: w.WriteBooleanValue(flag); break;
            case Guid id: w.WriteStringValue(id.ToString("D")); break;
            case DateTimeOffset instant: w.WriteStringValue(Instant(instant)); break;
            case DateTime stamp: w.WriteStringValue(Instant(new DateTimeOffset(DateTime.SpecifyKind(stamp, DateTimeKind.Utc)))); break;
            default: throw new NotSupportedException($"A {typeof(TValue).Name} is not a document value.");
        }
    }
}
