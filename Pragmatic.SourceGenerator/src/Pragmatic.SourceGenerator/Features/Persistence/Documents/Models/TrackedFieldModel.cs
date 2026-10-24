namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>One tracked field: the expression its snapshot keeps, and the statement that writes it in a <c>$set</c>.</summary>
/// <param name="Value">The field's value as the snapshot keeps it, read from <c>e</c>: a scalar by value, a list or an embedded document by its BSON.</param>
/// <param name="Write">The statement that writes the field to a <c>BsonWriter w</c> from <c>e</c>.</param>
internal sealed record TrackedFieldModel(string Value, string Write);
