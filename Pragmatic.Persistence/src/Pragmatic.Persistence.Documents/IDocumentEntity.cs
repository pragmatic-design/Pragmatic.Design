using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     What the generator adds to a <see cref="DocumentAttribute" /> class: its collection, its document written and read field by field
///     (no reflection), its key and its version. The runtime calls these as static members of the type, so a session's writes and a chain's
///     reads need no registry and work across assemblies.
/// </summary>
public interface IDocumentEntity<TSelf> where TSelf : IDocumentEntity<TSelf>
{
    static abstract string Collection { get; }

    /// <summary>The document's fields, <c>_id</c> first, into a document the caller opened and closes.</summary>
    static abstract void WriteFields(BsonWriter writer, TSelf entity);

    /// <summary>A document read back: the fields it holds set, the others left at their defaults (a projection reads fewer).</summary>
    static abstract TSelf ReadDocument(ReadOnlySpan<byte> document);

    /// <summary>The key's value under <paramref name="name" /> (<c>_id</c> in a filter).</summary>
    static abstract void WriteKey(BsonWriter writer, string name, TSelf entity);

    /// <summary>True when the type has a <see cref="VersionAttribute" /> property.</summary>
    static abstract bool Versioned { get; }

    static abstract long GetVersion(TSelf entity);

    static abstract void SetVersion(TSelf entity, long version);

    // Cosmos DB (JSON): the same document as an object, the key as "id" (a string), the version as "_v".

    /// <summary>The document as a JSON object, <c>id</c> first.</summary>
    static abstract void WriteJson(System.Text.Json.Utf8JsonWriter writer, TSelf entity);

    static abstract TSelf ReadJson(System.Text.Json.JsonElement document);

    /// <summary>The key as Cosmos DB's <c>id</c>.</summary>
    static abstract string Id(TSelf entity);

    /// <summary>The partition key's path (<c>/CustomerId</c>; <c>/id</c> when the type names none).</summary>
    static abstract string PartitionKeyPath { get; }

    /// <summary>The partition key's value, as one JSON value.</summary>
    static abstract void WritePartitionKey(System.Text.Json.Utf8JsonWriter writer, TSelf entity);

    /// <summary>A Pragmatic entity's filter families, applied to every read of it; none for a plain [Document].</summary>
    static virtual DocFamily[] Families => [];
}

/// <summary>What the generator adds to an <see cref="EmbeddedAttribute" /> class: its nested document written and read.</summary>
public interface IEmbeddedDocument<TSelf> where TSelf : IEmbeddedDocument<TSelf>
{
    /// <summary>The nested document's fields, into a document the caller opened and closes.</summary>
    static abstract void WriteFields(BsonWriter writer, TSelf value);

    static abstract TSelf ReadDocument(ReadOnlySpan<byte> document);

    /// <summary>The nested document's properties, into an object the caller opened and closes.</summary>
    static abstract void WriteJsonFields(System.Text.Json.Utf8JsonWriter writer, TSelf value);

    static abstract TSelf ReadJson(System.Text.Json.JsonElement document);
}

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
