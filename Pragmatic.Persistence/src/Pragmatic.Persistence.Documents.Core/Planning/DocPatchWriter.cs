namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     Writes a Cosmos DB patch's operations (the elements of its <c>operations</c> array) for one document of an update: a value from the chain's
///     slots, or one computed from <paramref name="entity" />, the document as it was read.
/// </summary>
public delegate void DocPatchWriter<in TEntity>(System.Text.Json.Utf8JsonWriter writer, TEntity entity, ReadOnlySpan<object?> slots);
