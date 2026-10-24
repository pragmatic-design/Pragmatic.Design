using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Conversion;

/// <summary>Reads one value at the reader's current element (a list's element reader).</summary>
public delegate TValue DocValueReader<out TValue>(ref BsonReader reader);
