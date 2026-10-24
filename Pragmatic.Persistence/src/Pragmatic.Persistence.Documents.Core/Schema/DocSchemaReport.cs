namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>Everything <see cref="DocSchema.EnsureAsync" /> did, element by element.</summary>
public sealed class DocSchemaReport(IReadOnlyList<DocSchemaChange> changes)
{
    public IReadOnlyList<DocSchemaChange> Changes { get; } = changes;

    /// <summary>True when the run created or changed nothing: the database held the schema already.</summary>
    public bool Unchanged => Changes.All(c => c.Outcome is DocSchemaOutcome.Unchanged or DocSchemaOutcome.NotApplied);

    /// <summary>The declared elements the backend could not apply, each with its reason.</summary>
    public IReadOnlyList<DocSchemaChange> NotApplied => [.. Changes.Where(c => c.Outcome == DocSchemaOutcome.NotApplied)];
}
