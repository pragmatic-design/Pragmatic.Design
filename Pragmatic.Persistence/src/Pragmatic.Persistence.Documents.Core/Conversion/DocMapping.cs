using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Conversion;

/// <summary>
///     What a generated reader and writer call for the fields that are not one BSON value: an embedded document, a list of them, a list of
///     values; and the numbers read whatever numeric type the server holds them as (a document written by another program may store an
///     integer where the model has a decimal).
/// </summary>
public static class DocMapping
{
    public static void WriteEmbedded<T>(BsonWriter w, string name, T? value) where T : IEmbeddedDocument<T>
    {
        if (value is null)
        {
            w.WriteNull(name);
            return;
        }

        w.StartDocument(name);
        T.WriteFields(w, value);
        w.EndDocument();
    }

    public static T ReadEmbedded<T>(ReadOnlySpan<byte> document) where T : IEmbeddedDocument<T> => T.ReadDocument(document);

    public static void WriteEmbeddedList<T>(BsonWriter w, string name, List<T>? values) where T : IEmbeddedDocument<T>
    {
        if (values is null)
        {
            w.WriteNull(name);
            return;
        }

        w.StartArray(name);
        for (var i = 0; i < values.Count; i++)
        {
            w.StartDocument(i);
            T.WriteFields(w, values[i]);
            w.EndDocument();
        }

        w.EndArray();
    }

    public static List<T> ReadEmbeddedList<T>(ReadOnlySpan<byte> array) where T : IEmbeddedDocument<T>
    {
        var list = new List<T>();
        var reader = new BsonReader(array);
        while (reader.ReadElement(out var type, out _))
        {
            if (type == BsonType.Document)
                list.Add(T.ReadDocument(reader.ReadDocument()));
            else
                reader.Skip();
        }

        return list;
    }

    public static void WriteList<TValue>(BsonWriter w, string name, List<TValue>? values, Action<BsonWriter, string, TValue> write)
    {
        if (values is null)
            w.WriteNull(name);
        else
            DocValues.WriteArray(w, name, values, write);
    }

    public static List<TValue> ReadList<TValue>(ReadOnlySpan<byte> array, DocValueReader<TValue> read)
    {
        var list = new List<TValue>();
        var reader = new BsonReader(array);
        while (reader.ReadElement(out var type, out _))
        {
            if (type == BsonType.Null)
            {
                reader.Skip();
                list.Add(default!);
            }
            else
            {
                list.Add(read(ref reader));
            }
        }

        return list;
    }

    public static int ReadInt32(ref BsonReader reader) => reader.Type == BsonType.Int32 ? reader.ReadInt32() : checked((int)reader.ReadNumber());

    public static long ReadInt64(ref BsonReader reader) => reader.Type switch
    {
        BsonType.Int64 => reader.ReadInt64(),
        BsonType.Int32 => reader.ReadInt32(),
        _ => checked((long)reader.ReadNumber()),
    };

    public static double ReadDouble(ref BsonReader reader) => reader.ReadNumber();

    public static decimal ReadDecimal(ref BsonReader reader) => reader.Type switch
    {
        BsonType.Decimal128 => reader.ReadDecimal(),
        BsonType.Int32 => reader.ReadInt32(),
        BsonType.Int64 => reader.ReadInt64(),
        _ => (decimal)reader.ReadNumber(),
    };

    /// <summary>A DateTime as the UTC instant stored.</summary>
    public static DateTime ReadDateTime(ref BsonReader reader) => reader.ReadDateTime().UtcDateTime;

    public static void Write(BsonWriter w, string name, DateTime? value)
    {
        if (value is { } v)
            DocValues.Write(w, name, v);
        else
            w.WriteNull(name);
    }
}
