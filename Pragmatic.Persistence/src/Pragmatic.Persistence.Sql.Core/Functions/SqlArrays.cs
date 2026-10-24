using System.Buffers;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     Array columns (<c>string[]</c>, <c>Guid[]</c>, <c>int[]</c>, <c>long[]</c>, or a <c>List</c> of them): a native array on PostgreSQL
///     (<c>text[]</c>, <c>uuid[]</c>, <c>integer[]</c>, <c>bigint[]</c>: Npgsql binds and reads the typed array), the JSON text of the array
///     elsewhere, written and read with the JSON writer and reader, no serializer (Native AOT clean). A Guid is its lowercase "D" text in JSON.
/// </summary>
public static class SqlArrays
{
    /// <summary>The value a column binds: the array itself on PostgreSQL, its JSON text elsewhere; null stays null.</summary>
    public static object? ToStore(SqlDialect dialect, IEnumerable<string>? values)
        => values is null ? null : dialect == SqlDialect.PostgreSql ? values as string[] ?? values.ToArray() : Json(values, static (w, v) => w.WriteStringValue(v));

    public static object? ToStore(SqlDialect dialect, IEnumerable<Guid>? values)
        => values is null ? null : dialect == SqlDialect.PostgreSql ? values as Guid[] ?? values.ToArray() : Json(values, static (w, v) => w.WriteStringValue(v.ToString("D")));

    public static object? ToStore(SqlDialect dialect, IEnumerable<int>? values)
        => values is null ? null : dialect == SqlDialect.PostgreSql ? values as int[] ?? values.ToArray() : Json(values, static (w, v) => w.WriteNumberValue(v));

    public static object? ToStore(SqlDialect dialect, IEnumerable<long>? values)
        => values is null ? null : dialect == SqlDialect.PostgreSql ? values as long[] ?? values.ToArray() : Json(values, static (w, v) => w.WriteNumberValue(v));

    public static string[] Strings(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.PostgreSql ? reader.GetFieldValue<string[]>(ordinal) : Parse(reader.GetString(ordinal), static (ref Utf8JsonReader r) => r.GetString()!);

    public static Guid[] Guids(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.PostgreSql ? reader.GetFieldValue<Guid[]>(ordinal) : Parse(reader.GetString(ordinal), static (ref Utf8JsonReader r) => Guid.Parse(r.GetString()!, CultureInfo.InvariantCulture));

    public static int[] Int32s(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.PostgreSql ? reader.GetFieldValue<int[]>(ordinal) : Parse(reader.GetString(ordinal), static (ref Utf8JsonReader r) => r.GetInt32());

    public static long[] Int64s(DbDataReader reader, int ordinal, SqlDialect dialect)
        => dialect == SqlDialect.PostgreSql ? reader.GetFieldValue<long[]>(ordinal) : Parse(reader.GetString(ordinal), static (ref Utf8JsonReader r) => r.GetInt64());

    private delegate T Element<T>(ref Utf8JsonReader reader);

    private static string Json<T>(IEnumerable<T> values, Action<Utf8JsonWriter, T> write)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            foreach (var value in values)
                write(w, value);
            w.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static T[] Parse<T>(string json, Element<T> read)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new FormatException($"An array column holds a JSON array, not: {json}");
        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(read(ref reader));
        return items.ToArray();
    }
}
