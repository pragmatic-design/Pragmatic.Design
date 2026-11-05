namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

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
/// <param name="Setter">
///     The accessor a read sets the property through, <c>Setter(e, value)</c>, when the partial cannot assign it: a base type's private setter seen
///     from a derived type (#539); null when the read assigns <c>e.Property</c>.
/// </param>
internal sealed record DocumentFieldModel(
    string PropertyName,
    string BsonNameLiteral,
    string BsonWrite,
    string BsonRead,
    string JsonNameLiteral,
    string JsonWrite,
    string JsonRead,
    bool IsKey,
    string? Setter = null)
{
    /// <summary>The statement that sets the property to <paramref name="value" /> on <c>e</c>.</summary>
    public string Assign(string value) => Setter is null ? $"e.{PropertyName} = {value};" : $"{Setter}(e, {value});";
}
