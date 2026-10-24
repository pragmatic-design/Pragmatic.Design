using System.Buffers.Binary;
using System.Text;

namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>
///     Reads one BSON document element by element, on a span: <see cref="ReadElement" /> gives the next element's type and name (UTF-8, to
///     compare without allocating: <c>name.SequenceEqual("Total"u8)</c>), then one of the value readers, or <see cref="Skip" />, consumes its
///     value. A nested document or array is <see cref="ReadDocument" />'s span, read by a reader of its own. The input is trusted to be
///     well-formed BSON the server sent; a length that runs past the document throws <see cref="FormatException" />.
/// </summary>
public ref struct BsonReader
{
    private readonly ReadOnlySpan<byte> _document;
    private int _position;
    private BsonType _type;

    public BsonReader(ReadOnlySpan<byte> document)
    {
        if (document.Length < 5)
            throw new FormatException("A BSON document has at least five bytes.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(document);
        if (length < 5 || length > document.Length || document[length - 1] != 0)
            throw new FormatException($"The BSON document's length ({length}) does not match its bytes ({document.Length}).");
        _document = document[..length];
        _position = 4;
        _type = BsonType.EndOfDocument;
    }

    /// <summary>The type of the element <see cref="ReadElement" /> read last.</summary>
    public readonly BsonType Type => _type;

    /// <summary>The offset in the document of the next byte to read: before a value, where the value starts.</summary>
    public readonly int Position => _position;

    /// <summary>Moves to the next element: false at the end of the document.</summary>
    public bool ReadElement(out BsonType type, out ReadOnlySpan<byte> name)
    {
        type = (BsonType)_document[_position++];
        _type = type;
        if (type == BsonType.EndOfDocument)
        {
            name = default;
            return false;
        }

        name = CString();
        return true;
    }

    public string ReadString()
    {
        var length = Int32();
        if (length < 1)
            throw new FormatException("A BSON string's length counts its terminating zero.");
        var text = Encoding.UTF8.GetString(Take(length)[..^1]);
        return text;
    }

    /// <summary>The string's UTF-8 bytes, without its terminating zero: to compare without decoding.</summary>
    public ReadOnlySpan<byte> ReadStringBytes() => Take(Int32())[..^1];

    public int ReadInt32() => Int32();

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public bool ReadBoolean() => Take(1)[0] != 0;

    public decimal ReadDecimal() => Decimal128.Read(Take(16));

    /// <summary>A UTC datetime as the instant it names (milliseconds since the Unix epoch).</summary>
    public DateTimeOffset ReadDateTime() => DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64());

    public ObjectId ReadObjectId() => new(Take(12));

    public ReadOnlySpan<byte> ReadBinary(out byte subtype)
    {
        var length = Int32();
        subtype = Take(1)[0];
        return Take(length);
    }

    /// <summary>A UUID: binary subtype 4 in RFC 4122 order.</summary>
    public Guid ReadGuid()
    {
        var bytes = ReadBinary(out var subtype);
        if (subtype != BsonBinarySubtype.Uuid || bytes.Length != 16)
            throw new FormatException($"A Guid is binary subtype 4 of 16 bytes, not subtype {subtype} of {bytes.Length}.");
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>A nested document or array: its whole encoding, for a reader of its own.</summary>
    public ReadOnlySpan<byte> ReadDocument()
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(_document[_position..]);
        return Take(length);
    }

    /// <summary>Any number as a double (Int32, Int64, Double, Decimal128): what the server's <c>ok</c> and counts are written as.</summary>
    public double ReadNumber() => _type switch
    {
        BsonType.Int32 => ReadInt32(),
        BsonType.Int64 => ReadInt64(),
        BsonType.Double => ReadDouble(),
        BsonType.Decimal128 => (double)ReadDecimal(),
        _ => throw new FormatException($"A {_type} is not a number."),
    };

    /// <summary>Consumes the value of the element just read, whatever its type.</summary>
    public void Skip()
    {
        switch (_type)
        {
            case BsonType.Double or BsonType.DateTime or BsonType.Timestamp or BsonType.Int64:
                Take(8);
                break;
            case BsonType.String or BsonType.JavaScript or BsonType.Symbol:
                Take(Int32());
                break;
            case BsonType.Document or BsonType.Array or BsonType.JavaScriptWithScope:
                ReadDocument();
                break;
            case BsonType.Binary:
                Take(Int32() + 1);
                break;
            case BsonType.ObjectId:
                Take(12);
                break;
            case BsonType.Boolean:
                Take(1);
                break;
            case BsonType.Int32:
                Take(4);
                break;
            case BsonType.Decimal128:
                Take(16);
                break;
            case BsonType.RegularExpression:
                CString();
                CString();
                break;
            case BsonType.DbPointer:
                Take(Int32());
                Take(12);
                break;
            case BsonType.Null or BsonType.Undefined or BsonType.MinKey or BsonType.MaxKey:
                break;
            default:
                throw new FormatException($"Unknown BSON type 0x{(byte)_type:X2}.");
        }
    }

    private int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    private ReadOnlySpan<byte> CString()
    {
        var end = _document[_position..].IndexOf((byte)0);
        if (end < 0)
            throw new FormatException("A BSON name runs past the document.");
        var name = _document.Slice(_position, end);
        _position += end + 1;
        return name;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _position + count > _document.Length)
            throw new FormatException("A BSON value runs past the document.");
        var span = _document.Slice(_position, count);
        _position += count;
        return span;
    }
}
