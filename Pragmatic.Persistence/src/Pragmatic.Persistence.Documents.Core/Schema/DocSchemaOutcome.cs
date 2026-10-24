namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>What <see cref="DocSchema.EnsureAsync" /> did with one element of a collection's schema.</summary>
public enum DocSchemaOutcome
{
    Created,
    Updated,
    Unchanged,

    /// <summary>Declared and not applied, the reason said: the backend cannot do it as declared (never skipped in silence).</summary>
    NotApplied,
}
