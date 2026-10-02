namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Pragmatic's canonical grid request format. All external grid frameworks
///     (DevExpress, AG Grid, PrimeNG, etc.) map INTO this format via
///     <see cref="IGridFilterAdapter{TExternalFormat}" />.
/// </summary>
/// <remarks>
///     <para>
///         The canonical format decouples grid UI concerns from query execution.
///         A source-generated bridge converts this runtime object into typed
///         <c>IQueryable&lt;T&gt;</c> operations at compile time, with zero reflection.
///     </para>
///     <para>
///         Flow: External Grid → Adapter → GridFilterRequest → SG Bridge → IQueryable
///     </para>
/// </remarks>
public record GridFilterRequest
{
    /// <summary>
    ///     Filter conditions to apply.
    /// </summary>
    public IReadOnlyList<FilterClause> Filters { get; init; } = [];

    /// <summary>
    ///     Sort specifications (multi-sort supported, order matters).
    /// </summary>
    public IReadOnlyList<SortClause> Sorts { get; init; } = [];

    /// <summary>
    ///     Grouping specifications.
    /// </summary>
    public IReadOnlyList<GroupClause> Groups { get; init; } = [];

    /// <summary>
    ///     Page number (1-based). Null = no paging.
    /// </summary>
    public int? Page { get; init; }

    /// <summary>
    ///     Number of items per page. Null = no paging.
    /// </summary>
    public int? PageSize { get; init; }
}
