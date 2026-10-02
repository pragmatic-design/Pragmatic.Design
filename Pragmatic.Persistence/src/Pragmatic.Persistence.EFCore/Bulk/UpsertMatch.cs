namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Strategy for matching existing entities during upsert.
/// </summary>
public enum UpsertMatch
{
    /// <summary>Match on primary key (PersistenceId).</summary>
    PrimaryKey,

    /// <summary>Match on [LogicKey] property (e.g., Email, Sku).</summary>
    LogicKey,
}
