namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     The page a grid asked for, as a <c>[GridAdapter]</c> reads it: its rows, and its total when the grid asked for one (DevExpress
///     <c>RequireTotalCount</c>), counted inside the same filters.
/// </summary>
public sealed record GridPage<T>(IReadOnlyList<T> Items, int? TotalCount);
