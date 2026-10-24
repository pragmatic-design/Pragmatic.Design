namespace Pragmatic.Persistence.Documents.Generator.Models;

/// <summary>
///     One field of a document type as its partial reads and writes it: the expressions the translation produced (a field's BSON and JSON
///     reads and writes), which the template places.
/// </summary>
/// <param name="PropertyName">The C# property.</param>
/// <param name="BsonNameLiteral">The field's name in BSON, as a C# string literal.</param>
/// <param name="BsonWrite">The statement that writes the field to a <c>BsonWriter w</c> from <c>e</c>.</param>
/// <param name="BsonRead">The expression that reads the field from a <c>BsonReader r</c>.</param>
/// <param name="JsonNameLiteral">The field's name in Cosmos DB's JSON, as a C# string literal.</param>
/// <param name="JsonWrite">The statement that writes the field to a <c>Utf8JsonWriter w</c> from <c>e</c>.</param>
/// <param name="JsonRead">The expression that reads the field from a <c>JsonElement v</c>.</param>
/// <param name="IsKey">The document's key (<c>_id</c>): written first in BSON, as <c>id</c> in JSON.</param>
internal sealed record DocumentFieldModel(
    string PropertyName,
    string BsonNameLiteral,
    string BsonWrite,
    string BsonRead,
    string JsonNameLiteral,
    string JsonWrite,
    string JsonRead,
    bool IsKey);
