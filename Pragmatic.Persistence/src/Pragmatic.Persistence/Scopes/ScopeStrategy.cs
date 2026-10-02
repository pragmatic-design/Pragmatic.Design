namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Determines how a <see cref="DataScopeRule{T}"/> is evaluated at query time.
/// </summary>
public enum ScopeStrategy
{
    /// <summary>
    ///     The scope is materialized into <c>AccessScopes</c> on entity create/update.
    ///     Fast queries (indexed column), but requires re-materialization when the rule changes.
    /// </summary>
    Materialized,

    /// <summary>
    ///     The scope expression is evaluated at query time (no materialization).
    ///     Always up-to-date, but may be slower for complex expressions.
    /// </summary>
    Computed,

    /// <summary>
    ///     Both: materialized for fast queries, but also verified at query time.
    ///     Best accuracy at the cost of performance.
    /// </summary>
    Hybrid
}
