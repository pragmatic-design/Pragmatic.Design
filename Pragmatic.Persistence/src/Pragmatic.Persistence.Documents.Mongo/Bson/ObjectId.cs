namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>A BSON ObjectId: twelve bytes, as the server generates them for a document written without an <c>_id</c>.</summary>
public readonly struct ObjectId : IEquatable<ObjectId>
{
    private readonly byte[]? _bytes;

    public ObjectId(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 12)
            throw new ArgumentException("An ObjectId is twelve bytes.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public ReadOnlySpan<byte> Bytes => _bytes ?? new byte[12];

    public void CopyTo(Span<byte> destination) => Bytes.CopyTo(destination);

    public bool Equals(ObjectId other) => Bytes.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj) => obj is ObjectId other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    public override string ToString() => Convert.ToHexStringLower(Bytes);

    public static bool operator ==(ObjectId left, ObjectId right) => left.Equals(right);

    public static bool operator !=(ObjectId left, ObjectId right) => !left.Equals(right);
}
