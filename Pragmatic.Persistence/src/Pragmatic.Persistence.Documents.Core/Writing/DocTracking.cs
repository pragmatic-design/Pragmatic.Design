using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Writing;

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
