using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     A <see cref="RollUpAttribute{TChild}" /> as the child's writes see it, registered by the generator with the parent's map: the parent's table,
///     key and aggregate column, the child's table, its foreign key and the column summed (none for a count). Its statement moves the aggregate of
///     the parents of the child rows a condition selects by those rows' contribution, read from the child's table: subtracted before a write that
///     changes or removes them, added after one that writes them. A delta, not a recompute: concurrent writers of the same parent add up.
/// </summary>
public sealed class SqlRollUp(string parentTable, string parentKey, string aggregate, string childTable, string foreignKey, string? amount)
{
    private static readonly ConcurrentDictionary<string, SqlRollUp[]> s_byChild = new();

    public string ParentTable { get; } = parentTable;

    public string ParentKey { get; } = parentKey;

    public string Aggregate { get; } = aggregate;

    public string ChildTable { get; } = childTable;

    public string ForeignKey { get; } = foreignKey;

    /// <summary>The child column summed; null for a count (each row is worth one).</summary>
    public string? Amount { get; } = amount;

    /// <summary>A [SoftDelete] child's flag column: a deleted row counts for nothing.</summary>
    public string? LiveColumn { get; init; }

    /// <summary>Adds the roll-up to the ones its child table feeds; the same parent column registered again (a module initialised twice) is not doubled.</summary>
    public static void Register(SqlRollUp rollUp)
        => s_byChild.AddOrUpdate(rollUp.ChildTable, _ => [rollUp], (_, known) => Array.Exists(known, k => k.ParentTable == rollUp.ParentTable && k.Aggregate == rollUp.Aggregate) ? known : [.. known, rollUp]);

    /// <summary>The roll-ups the rows of a table feed; empty when none.</summary>
    internal static SqlRollUp[] For(string childTable) => s_byChild.TryGetValue(childTable, out var rollUps) ? rollUps : [];

    /// <summary>
    ///     <c>UPDATE parent SET aggregate = aggregate ± (the contribution of the selected child rows under it) WHERE key IN (their parents)</c>.
    ///     <paramref name="rows" /> is a condition on the child's table, its columns unqualified (they resolve to the child's rows in both subqueries).
    /// </summary>
    public string Statement(SqlDialect dialect, bool add, string rows)
    {
        string Q(string name) => SqlDialects.Quote(dialect, name);
        var parent = Q(ParentTable);
        var column = Q(Aggregate);
        var contribution = Amount is null ? "COUNT(*)" : $"COALESCE(SUM(c.{Q(Amount)}), 0)";
        var live = LiveColumn is null ? "" : $" AND c.{Q(LiveColumn)} = {SqlDialects.ResolveBooleanLiterals("{bt:0}", dialect)}"; // a soft-deleted child counts for nothing
        return $"UPDATE {parent} SET {column} = COALESCE({column}, 0) {(add ? "+" : "-")} (SELECT {contribution} FROM {Q(ChildTable)} c WHERE c.{Q(ForeignKey)} = {parent}.{Q(ParentKey)} AND ({rows}){live}) "
               + $"WHERE {Q(ParentKey)} IN (SELECT c.{Q(ForeignKey)} FROM {Q(ChildTable)} c WHERE {rows})";
    }
}
