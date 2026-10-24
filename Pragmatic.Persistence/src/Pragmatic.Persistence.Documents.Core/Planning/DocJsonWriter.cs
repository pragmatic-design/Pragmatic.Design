using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>Writes JSON from the chain's slots: a Cosmos query's parameters (the elements of its array), or its partition key's value.</summary>
public delegate void DocJsonWriter(System.Text.Json.Utf8JsonWriter writer, ReadOnlySpan<object?> slots);
