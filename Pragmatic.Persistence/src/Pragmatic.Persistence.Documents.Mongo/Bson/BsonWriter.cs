using System.Buffers.Binary;
using System.Text;

namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>
///     Writes a BSON document into a growing buffer: <see cref="StartDocument()" /> and <see cref="EndDocument" /> around the elements, the
///     length of each document and array patched in when it ends. Element names are written as given (no NUL inside); an array's elements are
///     named by their index (<see cref="Write(int, string)" /> and its siblings). Reusable after <see cref="Reset" />.
/// </summary>
public sealed class BsonWriter
{
    private byte[] _buffer;
    private int _length;
    private readonly Stack<int> _starts = new();

    public BsonWriter(int capacity = 256) => _buffer = new byte[Math.Max(capacity, 16)];

    /// <summary>The bytes written so far: the whole document once the outermost <see cref="EndDocument" /> ran.</summary>
    public ReadOnlyMemory<byte> Written => _buffer.AsMemory(0, _length);

    /// <summary>The number of documents and arrays opened and not yet ended.</summary>
    public int Depth => _starts.Count;

    public void Reset()
    {
        _length = 0;
        _starts.Clear();
    }

    /// <summary>Opens the outermost document (or one that is an array's element, after <see cref="StartDocument(int)" />'s header).</summary>
    public void StartDocument()
    {
        _starts.Push(_length);
        Advance(4); // the length, patched at the end
    }

    public void StartDocument(string name)
    {
        Header(BsonType.Document, name);
        StartDocument();
    }

    public void StartDocument(int index)
    {
        Header(BsonType.Document, index);
        StartDocument();
    }

    public void StartArray(string name)
    {
        Header(BsonType.Array, name);
        StartDocument();
    }

    public void StartArray(int index)
    {
        Header(BsonType.Array, index);
        StartDocument();
    }

    /// <summary>Ends the innermost document or array: its terminating zero, then its length written at its start.</summary>
    public void EndDocument()
    {
        if (_starts.Count == 0)
            throw new InvalidOperationException("No document is open.");
        Byte(0);
        var start = _starts.Pop();
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(start), _length - start);
    }

    public void EndArray() => EndDocument();

    public void Write(string name, string value)
    {
        Header(BsonType.String, name);
        StringValue(value);
    }

    public void Write(int index, string value)
    {
        Header(BsonType.String, index);
        StringValue(value);
    }

    public void Write(string name, int value)
    {
        Header(BsonType.Int32, name);
        BinaryPrimitives.WriteInt32LittleEndian(Advance(4), value);
    }

    public void Write(int index, int value)
    {
        Header(BsonType.Int32, index);
        BinaryPrimitives.WriteInt32LittleEndian(Advance(4), value);
    }

    public void Write(string name, long value)
    {
        Header(BsonType.Int64, name);
        BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value);
    }

    public void Write(int index, long value)
    {
        Header(BsonType.Int64, index);
        BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value);
    }

    public void Write(string name, double value)
    {
        Header(BsonType.Double, name);
        BinaryPrimitives.WriteDoubleLittleEndian(Advance(8), value);
    }

    public void Write(int index, double value)
    {
        Header(BsonType.Double, index);
        BinaryPrimitives.WriteDoubleLittleEndian(Advance(8), value);
    }

    public void Write(string name, bool value)
    {
        Header(BsonType.Boolean, name);
        Byte(value ? (byte)1 : (byte)0);
    }

    public void Write(int index, bool value)
    {
        Header(BsonType.Boolean, index);
        Byte(value ? (byte)1 : (byte)0);
    }

    public void Write(string name, decimal value)
    {
        Header(BsonType.Decimal128, name);
        Decimal128.Write(Advance(16), value);
    }

    public void Write(int index, decimal value)
    {
        Header(BsonType.Decimal128, index);
        Decimal128.Write(Advance(16), value);
    }

    /// <summary>A UUID: binary subtype 4, the bytes in RFC 4122 order.</summary>
    public void Write(string name, Guid value)
    {
        Header(BsonType.Binary, name);
        GuidValue(value);
    }

    public void Write(int index, Guid value)
    {
        Header(BsonType.Binary, index);
        GuidValue(value);
    }

    /// <summary>A UTC datetime: the milliseconds since the Unix epoch (BSON keeps no finer grain: the ticks below a millisecond are dropped).</summary>
    public void Write(string name, DateTimeOffset value)
    {
        Header(BsonType.DateTime, name);
        BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value.ToUnixTimeMilliseconds());
    }

    public void Write(int index, DateTimeOffset value)
    {
        Header(BsonType.DateTime, index);
        BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value.ToUnixTimeMilliseconds());
    }

    public void Write(string name, ReadOnlySpan<byte> value, byte subtype = BsonBinarySubtype.Generic)
    {
        Header(BsonType.Binary, name);
        BinaryValue(value, subtype);
    }

    public void Write(string name, ObjectId value)
    {
        Header(BsonType.ObjectId, name);
        value.CopyTo(Advance(12));
    }

    public void WriteNull(string name) => Header(BsonType.Null, name);

    public void WriteNull(int index) => Header(BsonType.Null, index);

    /// <summary>A regular expression: the pattern and its options (letters, alphabetical: <c>i</c>, <c>m</c>, <c>s</c>, <c>x</c>…).</summary>
    public void WriteRegularExpression(string name, string pattern, string options)
    {
        Header(BsonType.RegularExpression, name);
        CString(pattern);
        CString(options);
    }

    /// <summary>A whole document already encoded (an entity written elsewhere, a document read back), embedded as it is.</summary>
    public void WriteDocument(string name, ReadOnlySpan<byte> document)
    {
        Header(BsonType.Document, name);
        document.CopyTo(Advance(document.Length));
    }

    public void WriteDocument(int index, ReadOnlySpan<byte> document)
    {
        Header(BsonType.Document, index);
        document.CopyTo(Advance(document.Length));
    }

    private void Header(BsonType type, string name)
    {
        Byte((byte)type);
        CString(name);
    }

    private void Header(BsonType type, int index)
    {
        Byte((byte)type);
        index.TryFormat(Advance(11), out var written, default, System.Globalization.CultureInfo.InvariantCulture);
        _length -= 11 - written;
        Byte(0);
    }

    private void StringValue(string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteInt32LittleEndian(Advance(4), count + 1);
        Encoding.UTF8.GetBytes(value, Advance(count));
        Byte(0);
    }

    private void GuidValue(Guid value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Advance(4), 16);
        Byte(BsonBinarySubtype.Uuid);
        value.TryWriteBytes(Advance(16), bigEndian: true, out _);
    }

    private void BinaryValue(ReadOnlySpan<byte> value, byte subtype)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Advance(4), value.Length);
        Byte(subtype);
        value.CopyTo(Advance(value.Length));
    }

    private void CString(string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        var span = Advance(count);
        Encoding.UTF8.GetBytes(value, span);
        if (span.Contains((byte)0))
            throw new ArgumentException($"A BSON name or pattern holds no NUL character: '{value}'.", nameof(value));
        Byte(0);
    }

    private void Byte(byte value) => Advance(1)[0] = value;

    private Span<byte> Advance(int count)
    {
        if (_length + count > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        var span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }
}
