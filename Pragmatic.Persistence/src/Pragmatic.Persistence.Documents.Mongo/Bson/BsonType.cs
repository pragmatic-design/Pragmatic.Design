namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>The element types of the BSON specification (bsonspec.org), by their tag byte.</summary>
public enum BsonType : byte
{
    EndOfDocument = 0x00,
    Double = 0x01,
    String = 0x02,
    Document = 0x03,
    Array = 0x04,
    Binary = 0x05,
    Undefined = 0x06,
    ObjectId = 0x07,
    Boolean = 0x08,
    DateTime = 0x09,
    Null = 0x0A,
    RegularExpression = 0x0B,
    DbPointer = 0x0C,
    JavaScript = 0x0D,
    Symbol = 0x0E,
    JavaScriptWithScope = 0x0F,
    Int32 = 0x10,
    Timestamp = 0x11,
    Int64 = 0x12,
    Decimal128 = 0x13,
    MaxKey = 0x7F,
    MinKey = 0xFF,
}

/// <summary>The binary subtypes this driver writes and reads.</summary>
public static class BsonBinarySubtype
{
    public const byte Generic = 0x00;

    /// <summary>A UUID in RFC 4122 byte order (big-endian), the standard representation of a <see cref="Guid" />.</summary>
    public const byte Uuid = 0x04;
}
