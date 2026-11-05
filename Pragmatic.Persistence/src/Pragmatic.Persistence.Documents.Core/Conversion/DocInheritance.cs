using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Conversion;

/// <summary>
///     What a hierarchy's generated reader and writer call (a Pragmatic entity hierarchy kept in its root's collection): the type a document's
///     discriminator names, and a derived type's own reader and writer reached from the type above it, which as static members of an interface
///     are called through a type parameter.
/// </summary>
public static class DocInheritance
{
    public static void WriteFields<T>(BsonWriter w, T entity) where T : IDocumentEntity<T> => T.WriteFields(w, entity);

    public static T ReadDocument<T>(ReadOnlySpan<byte> document) where T : IDocumentEntity<T> => T.ReadDocument(document);

    public static void WriteJson<T>(Utf8JsonWriter w, T entity) where T : IDocumentEntity<T> => T.WriteJson(w, entity);

    public static T ReadJson<T>(JsonElement document) where T : IDocumentEntity<T> => T.ReadJson(document);

    /// <summary>The text of the document's top-level <paramref name="field" />; null when it has none, or not as text.</summary>
    public static string? Discriminator(ReadOnlySpan<byte> document, ReadOnlySpan<byte> field)
    {
        var r = new BsonReader(document);
        while (r.ReadElement(out var type, out var name))
        {
            if (type == BsonType.String && name.SequenceEqual(field))
                return r.ReadString();
            r.Skip();
        }

        return null;
    }

    /// <summary>The text of the document's top-level <paramref name="field" />; null when it has none, or not as text.</summary>
    public static string? Discriminator(JsonElement document, string field)
        => document.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
