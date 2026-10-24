using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>Writes a filter's conditions (the elements of the filter document) from the chain's slots.</summary>
public delegate void DocFilterWriter(BsonWriter writer, ReadOnlySpan<object?> slots);
