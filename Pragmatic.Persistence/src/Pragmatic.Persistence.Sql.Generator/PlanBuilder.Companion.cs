using System.Globalization;
using System.Text;
using System.Linq;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A companion statement with a Take per parent (<c>Include(c => c.Orders.OrderBy(o => o.CreatedAt).Take(3))</c>): the child's rows
///     numbered by a window partitioned by the foreign key and ordered as the Include says, in a derived table, the outer statement
///     keeping the first <c>n</c> of each parent; the to-one includes join the derived table. One statement, in the same command.
/// </summary>
internal sealed partial class PlanBuilder
{
    /// <summary>
    ///     <c>SELECT cols FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t."FK" ORDER BY …) AS rn FROM "Child" t WHERE (t."FK" IN (parent keys)) AND
    ///     (filter…)) t [joins] WHERE (t.rn &lt;= n) ORDER BY t."FK", t.rn</c>. The predicates (the filter, the rules, a derived type's) go inside the
    ///     window with the child's alias; the ordering too, so the rows come out grouped by parent in the order taken.
    /// </summary>
    private string TakenStatement(SqlDialect dialect, string selectList, int take, bool keysOnly)
    {
        var inner = new StringBuilder();
        inner.Append("SELECT t.*, ROW_NUMBER() OVER (PARTITION BY ").Append(ForeignKeyList()).Append(" ORDER BY ").Append(string.Join(", ", _orderings)).Append(") AS rn");
        inner.Append(" FROM \"").Append(_entity.Table).Append("\" t");
        inner.Append(" WHERE (").Append(ParentRows(dialect, "t")).Append(')');
        foreach (var predicate in _predicates)
            inner.Append(" AND (").Append(predicate).Append(')');

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(selectList).Append(" FROM (").Append(inner).Append(") t");
        foreach (var join in _joins)
            sql.Append(' ').Append(Inner(join));
        sql.Append(" WHERE (t.rn <= ").Append(take.ToString(CultureInfo.InvariantCulture)).Append(')');
        if (!keysOnly)
            sql.Append(" ORDER BY ").Append(ForeignKeyList()).Append(", t.rn"); // as the keys of a companion below, the order means nothing (and SQL Server refuses it)
        return ForDialect(Resolve(sql.ToString(), dialect), dialect);
    }

    /// <summary>
    ///     The companion's rows under the parent statement's: <c>t."FK" IN (its keys)</c>; for a composite parent <c>EXISTS</c> over its key rows (k0, k1, …)
    ///     matched column by column (SQL Server has no row-value IN).
    /// </summary>
    private string ParentRows(SqlDialect dialect, string alias)
    {
        var parent = _parent!.Statement(dialect, _parentKey!, keysOnly: true);
        if (_foreignKeys is null)
            return $"{alias}.\"{_foreignKey}\" IN ({SqlDialects.KeySubquery(dialect, parent)})";
        return $"EXISTS (SELECT 1 FROM ({parent}) pk WHERE {string.Join(" AND ", _foreignKeys.Select((fk, i) => $"pk.k{i} = {alias}.\"{fk}\""))})";
    }

    /// <summary>The foreign key columns under the alias <c>t</c>, comma-separated: a window's partition, an ordering.</summary>
    private string ForeignKeyList() => string.Join(", ", (_foreignKeys ?? [_foreignKey!]).Select(fk => $"t.\"{fk}\""));
}
