using System.Text;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The values the generated code writes and reads: one overload per kind the documents hold, a null written as BSON null, an enum as its
///     Int32, a list as an array (for <c>$in</c>), a string turned into an anchored or unanchored regular expression with every metacharacter
///     escaped (Contains, StartsWith, EndsWith match the text as it is, case-sensitive like LINQ's ordinal comparison).
/// </summary>
public static class DocValues
{
    public static void Write(BsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.Write(name, value);
    }

    public static void Write(BsonWriter w, string name, int value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, long value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, double value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, decimal value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, bool value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, Guid value) => w.Write(name, value);

    public static void Write(BsonWriter w, string name, DateTimeOffset value) => w.Write(name, value);

    /// <summary>A DateTime as the UTC instant it names (a local or unspecified one is taken as UTC, as BSON has no zone).</summary>
    public static void Write(BsonWriter w, string name, DateTime value) => w.Write(name, new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    public static void Write(BsonWriter w, string name, int? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, long? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, double? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, decimal? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, bool? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, Guid? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    public static void Write(BsonWriter w, string name, DateTimeOffset? value)
    {
        if (value is { } v)
            w.Write(name, v);
        else
            w.WriteNull(name);
    }

    /// <summary>A list of values as an array (the operand of <c>$in</c>), each by its own kind.</summary>
    public static void WriteArray<TValue>(BsonWriter w, string name, IEnumerable<TValue> values, Action<BsonWriter, string, TValue> write)
    {
        ArgumentNullException.ThrowIfNull(values);
        w.StartArray(name);
        var i = 0;
        foreach (var value in values)
            write(w, (i++).ToString(System.Globalization.CultureInfo.InvariantCulture), value);
        w.EndArray();
    }

    /// <summary>
    ///     <c>{ $regex: pattern }</c> for a text match: the text escaped, anchored at the start (StartsWith), the end (EndsWith), both (an
    ///     equality), or neither (Contains); <c>$options: "i"</c> when the case is ignored (a field's <c>ToLower()</c> in the lambda).
    /// </summary>
    public static void WriteMatch(BsonWriter w, string? text, bool start, bool end, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(text); // LINQ throws for a null argument of Contains/StartsWith/EndsWith: so does the query
        var pattern = new StringBuilder(text.Length + 4);
        if (start)
            pattern.Append('^');
        foreach (var c in text)
        {
            if (c is '\\' or '^' or '$' or '.' or '|' or '?' or '*' or '+' or '(' or ')' or '[' or ']' or '{' or '}' or '-' or '/' or '#' or ' ')
                pattern.Append('\\');
            pattern.Append(c);
        }

        if (end)
            pattern.Append('$');
        w.Write("$regex", pattern.ToString());
        if (ignoreCase)
            w.Write("$options", "i");
    }

    /// <summary>A scalar read from an aggregate's reply (<c>$max</c>, <c>$min</c>, <c>$sum</c>) as the selector's type.</summary>
    public static TValue ReadScalar<TValue>(BsonDocument document, string name)
    {
        var reader = document.Reader();
        while (reader.ReadElement(out var type, out var current))
        {
            if (!current.SequenceEqual(Encoding.UTF8.GetBytes(name)))
            {
                reader.Skip();
                continue;
            }

            if (type == BsonType.Null)
                return default!;
            object value = typeof(TValue) == typeof(decimal) || typeof(TValue) == typeof(decimal?) ? type == BsonType.Decimal128 ? reader.ReadDecimal() : (decimal)reader.ReadNumber()
                : typeof(TValue) == typeof(int) || typeof(TValue) == typeof(int?) ? (int)reader.ReadNumber()
                : typeof(TValue) == typeof(long) || typeof(TValue) == typeof(long?) ? type == BsonType.Int64 ? reader.ReadInt64() : (long)reader.ReadNumber()
                : typeof(TValue) == typeof(double) || typeof(TValue) == typeof(double?) ? reader.ReadNumber()
                : typeof(TValue) == typeof(DateTimeOffset) || typeof(TValue) == typeof(DateTimeOffset?) ? reader.ReadDateTime()
                : typeof(TValue) == typeof(string) ? reader.ReadString()
                : typeof(TValue) == typeof(Guid) || typeof(TValue) == typeof(Guid?) ? reader.ReadGuid()
                : throw new NotSupportedException($"An aggregate of {typeof(TValue).Name} is not read back by this version.");
            return (TValue)value;
        }

        return default!;
    }
}
