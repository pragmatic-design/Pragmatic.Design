namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>Everything <see cref="DocSchema.EnsureAsync" /> did, element by element.</summary>
public sealed class DocSchemaReport(IReadOnlyList<DocSchemaChange> changes)
{
    public IReadOnlyList<DocSchemaChange> Changes { get; } = changes;

    /// <summary>True when the run created or changed nothing: the database held the schema already.</summary>
    public bool Unchanged => Changes.All(c => c.Outcome is DocSchemaOutcome.Unchanged or DocSchemaOutcome.NotApplied or DocSchemaOutcome.MutationOnly);

    /// <summary>The declared elements the backend could not apply, each with its reason.</summary>
    public IReadOnlyList<DocSchemaChange> NotApplied => [.. Changes.Where(c => c.Outcome == DocSchemaOutcome.NotApplied)];

    /// <summary>The declared refusals the backend has no means for, held by the mutation's check only, each with its reason: not a failure.</summary>
    public IReadOnlyList<DocSchemaChange> MutationOnly => [.. Changes.Where(c => c.Outcome == DocSchemaOutcome.MutationOnly)];
}
