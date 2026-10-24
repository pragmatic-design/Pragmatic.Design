namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>One element of the schema (a collection, an index, a unique key, the indexing policy) and its outcome.</summary>
public sealed record DocSchemaChange(string Collection, string Element, DocSchemaOutcome Outcome, string? Reason = null);
