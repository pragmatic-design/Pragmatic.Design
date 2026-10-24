using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>A write of a stale copy: the stored document's version is not the one the object holds (or the document is gone).</summary>
public sealed class DocConcurrencyException : Exception
{
    public DocConcurrencyException(string message) : base(message)
    {
    }

    public DocConcurrencyException(string collection, string message) : base(message) => Collection = collection;

    /// <summary>The collection of the document refused, when the write named it.</summary>
    public string? Collection { get; }
}
