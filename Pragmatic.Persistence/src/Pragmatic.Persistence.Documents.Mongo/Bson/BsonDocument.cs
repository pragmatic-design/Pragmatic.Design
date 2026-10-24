using System.Buffers.Binary;
using System.Text;

namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>
///     A BSON document kept as its bytes, read by name when asked: what a command's reply and a cursor's batch are. A nested document or array
///     is a slice of the same bytes, nothing copied. Each lookup walks the elements (replies are small; entities are read by their generated
///     readers with a <see cref="BsonReader" />, not through this).
/// </summary>
public sealed class BsonDocument(ReadOnlyMemory<byte> bytes)
{
    public ReadOnlyMemory<byte> Bytes { get; } = bytes;

    public BsonReader Reader() => new(Bytes.Span);

    public BsonType? TypeOf(string name) => Find(name, out var type, out _, out _) ? type : null;

    public bool TryGetNumber(string name, out double value)
    {
        value = 0;
        if (!Find(name, out var type, out var start, out var length))
            return false;
        var span = Bytes.Span.Slice(start, length);
        switch (type)
        {
            case BsonType.Int32:
                value = BinaryPrimitives.ReadInt32LittleEndian(span);
                return true;
            case BsonType.Int64:
                value = BinaryPrimitives.ReadInt64LittleEndian(span);
                return true;
            case BsonType.Double:
                value = BinaryPrimitives.ReadDoubleLittleEndian(span);
                return true;
            case BsonType.Decimal128:
                value = (double)Decimal128.Read(span);
                return true;
            default:
                return false;
        }
    }

    public bool TryGetInt64(string name, out long value)
    {
        value = 0;
        if (!TryGetNumber(name, out var number) || number != Math.Floor(number))
            return false;
        // A cursor id is an Int64 that a double would round: read it exactly when it is one.
        if (Find(name, out var type, out var start, out _) && type == BsonType.Int64)
        {
            value = BinaryPrimitives.ReadInt64LittleEndian(Bytes.Span[start..]);
            return true;
        }

        value = (long)number;
        return true;
    }

    public bool TryGetString(string name, out string value)
    {
        value = "";
        if (!Find(name, out var type, out var start, out var length) || type != BsonType.String)
            return false;
        value = Encoding.UTF8.GetString(Bytes.Span.Slice(start + 4, length - 5));
        return true;
    }

    public bool TryGetBoolean(string name, out bool value)
    {
        value = false;
        if (!Find(name, out var type, out var start, out _) || type != BsonType.Boolean)
            return false;
        value = Bytes.Span[start] != 0;
        return true;
    }

    /// <summary>A nested document or array (an array's elements are named "0", "1"…: <see cref="Elements" /> walks them in order).</summary>
    public bool TryGetDocument(string name, out BsonDocument value)
    {
        value = null!;
        if (!Find(name, out var type, out var start, out var length) || type is not (BsonType.Document or BsonType.Array))
            return false;
        value = new BsonDocument(Bytes.Slice(start, length));
        return true;
    }

    public bool TryGetBinary(string name, out ReadOnlyMemory<byte> value)
    {
        value = default;
        if (!Find(name, out var type, out var start, out var length) || type != BsonType.Binary)
            return false;
        value = Bytes.Slice(start + 5, length - 5);
        return true;
    }

    /// <summary>The documents of an array of documents, in order (other elements skipped).</summary>
    public List<BsonDocument> Documents()
    {
        var documents = new List<BsonDocument>();
        var reader = Reader();
        while (reader.ReadElement(out var type, out _))
        {
            var start = reader.Position;
            reader.Skip();
            if (type == BsonType.Document)
                documents.Add(new BsonDocument(Bytes[start..reader.Position]));
        }

        return documents;
    }

    /// <summary>The element names in order.</summary>
    public List<string> Names()
    {
        var names = new List<string>();
        var reader = Reader();
        while (reader.ReadElement(out _, out var name))
        {
            names.Add(Encoding.UTF8.GetString(name));
            reader.Skip();
        }

        return names;
    }

    public List<(string Name, BsonType Type)> Elements()
    {
        var elements = new List<(string, BsonType)>();
        var reader = Reader();
        while (reader.ReadElement(out var type, out var name))
        {
            elements.Add((Encoding.UTF8.GetString(name), type));
            reader.Skip();
        }

        return elements;
    }

    private bool Find(string name, out BsonType type, out int start, out int length)
    {
        Span<byte> wanted = stackalloc byte[Encoding.UTF8.GetMaxByteCount(name.Length)];
        wanted = wanted[..Encoding.UTF8.GetBytes(name, wanted)];
        var reader = Reader();
        while (reader.ReadElement(out type, out var current))
        {
            start = reader.Position;
            reader.Skip();
            if (current.SequenceEqual(wanted))
            {
                length = reader.Position - start;
                return true;
            }
        }

        start = 0;
        length = 0;
        return false;
    }
}
