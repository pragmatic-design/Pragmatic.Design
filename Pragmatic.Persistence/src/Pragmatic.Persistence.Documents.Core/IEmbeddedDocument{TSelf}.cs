using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

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
