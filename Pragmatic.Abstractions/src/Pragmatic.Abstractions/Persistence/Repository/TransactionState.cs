namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Lifecycle state of the ambient transaction owned by an <see cref="IUnitOfWork"/>.
///     Lets callers query/branch before issuing commit, rollback, or savepoint operations.
/// </summary>
public enum TransactionState
{
    /// <summary>No transaction has been started on this unit of work.</summary>
    None = 0,

    /// <summary>A transaction is open and accepting work (not yet committed or rolled back).</summary>
    Active = 1,

    /// <summary>The transaction was committed successfully.</summary>
    Committed = 2,

    /// <summary>The transaction was rolled back.</summary>
    RolledBack = 3,
}
