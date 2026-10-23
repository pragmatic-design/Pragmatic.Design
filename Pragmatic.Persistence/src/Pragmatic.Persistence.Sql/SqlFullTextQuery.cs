namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The query of <c>Sql.FullText</c>/<c>FullTextRank</c> on a <c>[FullText]</c> column, as the plan binds it: turned into the engine's condition
///     with every word required when the parameter is bound (<see cref="SqlValue.ToDatabase" />), so the five engines answer the same rows.
/// </summary>
public sealed class SqlFullTextQuery(string? text)
{
    public string? Text { get; } = text;

    public static SqlFullTextQuery Of(object? value) => new(value as string ?? value?.ToString());

    /// <summary>The condition on the dialect; null for a null query.</summary>
    internal string? Condition(SqlDialect dialect) => Text is null ? null : SqlCatalog.FullTextCondition(dialect, Text);
}
