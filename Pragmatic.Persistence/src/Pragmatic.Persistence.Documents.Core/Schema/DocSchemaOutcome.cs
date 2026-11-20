namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>What <see cref="DocSchema.EnsureAsync" /> did with one element of a collection's schema.</summary>
public enum DocSchemaOutcome
{
    Created,
    Updated,
    Unchanged,

    /// <summary>Declared and not applied, the reason said: the backend cannot do it as declared (never skipped in silence).</summary>
    NotApplied,

    /// <summary>
    ///     Declared and held by the mutation's check only, the reason said: a refusal the backend has no means for (a temporal relation's on
    ///     Cosmos DB, #900), as the Sql engine leaves a partial index out on MySQL or Oracle and reports it (PRAG3039). Reported, and not a failure:
    ///     the rule still holds where it is written through the mutation, only a write past it is not refused by the store.
    /// </summary>
    MutationOnly,
}
