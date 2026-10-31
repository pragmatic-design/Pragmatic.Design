namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     One member of an owned type as its codec reads and writes it: the field's expressions as a document type's (<see cref="DocumentFieldModel" />),
///     and the member's type, which the codec's reader holds the value in until it builds the object.
/// </summary>
/// <param name="PropertyName">The C# property.</param>
/// <param name="TypeName">The property's type, fully qualified with its nullable annotation: the local the reader keeps the value in.</param>
/// <param name="BsonNameLiteral">The field's name in BSON, as a C# string literal.</param>
/// <param name="BsonWrite">The statement that writes the field to a <c>BsonWriter w</c> from <c>e</c>.</param>
/// <param name="BsonRead">The expression that reads the field from a <c>BsonReader r</c>.</param>
/// <param name="JsonNameLiteral">The field's name in Cosmos DB's JSON, as a C# string literal.</param>
/// <param name="JsonWrite">The statement that writes the field to a <c>Utf8JsonWriter w</c> from <c>e</c>.</param>
/// <param name="JsonRead">The expression that reads the field from a <c>JsonElement v</c>.</param>
internal sealed record OwnedCodecFieldModel(
    string PropertyName,
    string TypeName,
    string BsonNameLiteral,
    string BsonWrite,
    string BsonRead,
    string JsonNameLiteral,
    string JsonWrite,
    string JsonRead);
