using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     What the generator adds to a Pragmatic entity stored as a document, for a unit of work's change detection: a snapshot of its fields as read,
///     the fields that differ from it (a bit each, in the order of the snapshot), and those fields written as a MongoDB <c>$set</c>. The key and
///     the version are not tracked: the key never changes, the version moves by itself. No reflection: one member access per field.
/// </summary>
public interface IDocumentTracked<TSelf> : IDocumentEntity<TSelf> where TSelf : IDocumentTracked<TSelf>
{
    /// <summary>The tracked fields as they are now: a scalar by value, a list or an embedded document as its BSON (<see cref="DocTracking.Bytes{T}" />).</summary>
    static abstract object?[] Snapshot(TSelf entity);

    /// <summary>The tracked fields that differ from <paramref name="snapshot" />, a bit each; 0 when none does.</summary>
    static abstract ulong Changed(TSelf entity, object?[] snapshot);

    /// <summary>The fields of <paramref name="mask" />, each under its name, into a document the caller opened and closes (the <c>$set</c>).</summary>
    static abstract void WriteSet(BsonWriter writer, TSelf entity, ulong mask);
}

/// <summary>The helpers of the generated change detection.</summary>
public static class DocTracking
{
    /// <summary>A field written alone in a document: how a list or an embedded document is kept in a snapshot and compared.</summary>
    public static byte[] Bytes<T>(T entity, Action<BsonWriter, T> write)
    {
        var w = new BsonWriter();
        w.StartDocument();
        write(w, entity);
        w.EndDocument();
        return w.Written.ToArray();
    }

    /// <summary>The same value: equal scalars, or the same bytes for a list or an embedded document.</summary>
    public static bool Same(object? before, object? now)
        => before is byte[] a && now is byte[] b ? a.AsSpan().SequenceEqual(b) : Equals(before, now);
}
