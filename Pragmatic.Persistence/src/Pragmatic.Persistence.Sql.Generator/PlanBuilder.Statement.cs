using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The statement text of a plan: the SELECT per dialect, with its joins, predicates, groups, ordering and paging; the companions after it.</summary>
internal sealed partial class PlanBuilder
{
    /// <summary>The command text for a dialect: the statement <see cref="TryFinish" /> settled, then one per collection include, in the loader's order.</summary>
    public string Sql(SqlDialect dialect)
    {
        if (IsCommand)
            return CommandSql(dialect);
        var text = new StringBuilder(Statement(dialect, _selectList));
        foreach (var child in _children)
            text.Append("; ").Append(child.Statement(dialect, child._selectList));
        return text.ToString();
    }

    /// <summary>
    ///     One SELECT with the given select list: the root's, with its joins, predicates, groups, ordering and paging; or a
    ///     companion's, whose first predicate is the subquery of its parent statement selecting the parent key (the
    ///     parent's whole shape, paging included, so only the loaded rows' children come back). As that subquery
    ///     (<paramref name="keysOnly" />) the ordering goes only with the paging it bounds: alone it means nothing in an IN list,
    ///     and SQL Server refuses it there.
    /// </summary>
    private string Statement(SqlDialect dialect, string selectList, bool keysOnly = false)
    {
        if (_members is not null && IsFlatCompound(dialect))
            return (_hoisted is not null && _parent is null ? WithClause(dialect) : "") + Compound(dialect); // the set operation without a wrapper: ORDER BY and paging on the compound itself; its members' Withs first
        if (_parent is not null && _perParentTake is { } take)
            return TakenStatement(dialect, selectList, take, keysOnly); // a companion with a Take per parent: the window form
        var sql = new StringBuilder();
        if ((_cte is not null || _hoisted is not null) && _parent is null && !_cteHoisted)
            sql.Append(WithClause(dialect)); // the named query first; a companion's subquery reads the statement without it (the clause does not nest on SQL Server)
        sql.Append("SELECT ");
        if (_distinct)
            sql.Append("DISTINCT ");
        sql.Append(selectList);
        if (_members is not null)
            AppendMembers(sql, dialect); // a set operation: the members as a derived table
        else if (_cte is not null)
            sql.Append(" FROM ").Append(CteName).Append(' ').Append(_alias);
        else if (_rawFrom is not null)
            sql.Append(" FROM ").Append(_rawFrom).Append(' ').Append(_alias); // a table-valued function: the source itself, its arguments bound
        else if (_raw is null)
        {
            if (_asOfParameter is not null && _entity.Temporal && dialect is SqlDialect.PostgreSql or SqlDialect.Sqlite or SqlDialect.MySql && _parent is null)
                sql.Append(" FROM (SELECT * FROM \"").Append(_entity.Table).Append("History\" WHERE \"ValidFrom\" <= ").Append(_asOfParameter).Append(" AND (\"ValidTo\" IS NULL OR \"ValidTo\" > ").Append(_asOfParameter).Append("))"); // the versions valid at the instant
            else
                sql.Append(" FROM \"").Append(_entity.Table).Append('"');
            if (_asOfParameter is not null && dialect == SqlDialect.SqlServer && _parent is null)
                sql.Append(" FOR SYSTEM_TIME AS OF ").Append(_asOfParameter); // the system-versioned table at the instant; the companions read the current children
            if (_asOfParameter is not null && dialect == SqlDialect.Oracle && _parent is null)
                sql.Append(" AS OF TIMESTAMP CAST(").Append(_asOfParameter).Append(" AS TIMESTAMP)"); // Flashback Query: any table, from the undo kept (the instant mapped to an SCN, a few seconds' grain)
            sql.Append(' ').Append(_alias);
            if (_lock >= 0 && dialect == SqlDialect.SqlServer && _parent is null)
                sql.Append(" WITH (UPDLOCK, ROWLOCK").Append(_lock == 1 ? ", READPAST)" : _lock == 2 ? ", NOWAIT)" : ")"); // the lock as a table hint on the root
        }
        else
            sql.Append(" FROM (").Append(_raw).Append(") t"); // the statement as written at the call site; its double-quoted identifiers get the dialect's quoting like the fragments'
        foreach (var join in _joins)
            sql.Append(' ').Append(Inner(join));
        var first = true;
        var named = false;
        if (_parent is not null)
        {
            sql.Append(" WHERE (").Append(ParentRows(dialect, _foreignKeyAlias)).Append(')');
            first = false;
        }

        foreach (var predicate in _predicates)
        {
            sql.Append(first ? " WHERE (" : " AND (").Append(predicate).Append(')');
            first = false;
        }

        if (KeysetPredicate(dialect) is { } keyset)
        {
            sql.Append(first ? " WHERE (" : " AND (").Append(keyset).Append(')');
            first = false;
        }

        if (_parent is not null && _orderings.Count > 0 && !keysOnly)
            sql.Append(" ORDER BY ").Append(string.Join(", ", _orderings)); // a companion ordered as the Include says: the loader keeps the order per parent
        if (_parent is null)
        {
            if (_groupKey is not null)
                sql.Append(" GROUP BY ").Append(_groupKey.GroupBy);
            for (var i = 0; i < _having.Count; i++)
                sql.Append(i == 0 ? " HAVING (" : " AND (").Append(_having[i]).Append(')');
            sql.Append(WindowsMarker); // the WINDOW clause's place: filled once the dialect pieces are resolved (a window inside a dialect's function shows then)
            named = true;
            if (!(_terminal == "CountAsync" || IsAggregate))
            {
                var paged = _skipParameter is not null || _limit is not null;
                if (_orderings.Count > 0 && !IsScalar && (!keysOnly || paged))
                    sql.Append(" ORDER BY ").Append(string.Join(", ", _orderings));
                else if ((_skipParameter is not null || _limit is not null) && dialect == SqlDialect.SqlServer)
                    sql.Append(" ORDER BY (SELECT NULL)"); // OFFSET/FETCH needs one; an unordered page is the caller's choice
                sql.Append(SqlDialects.Paging(dialect, _skipParameter, _limit));
            }

            if (_lock >= 0 && dialect is SqlDialect.PostgreSql or SqlDialect.MySql or SqlDialect.Oracle)
                sql.Append(_lock == 1 ? " FOR UPDATE SKIP LOCKED" : _lock == 2 ? " FOR UPDATE NOWAIT" : " FOR UPDATE"); // after the paging, as PostgreSQL, MySQL and Oracle want it
        }

        var text = Resolve(sql.ToString(), dialect);
        if (dialect == SqlDialect.SqlServer && _parent is null && _members is null)
        {
            // RANGE frames with an offset: correlated subqueries over the table under the statement's own predicates (the rules among them).
            var predicates = _predicates.Select(p => Resolve(p, dialect)).ToList();
            if (KeysetPredicate(dialect) is { } cursor)
                predicates.Add(Resolve(cursor, dialect));
            text = SqlDialects.RangeSubqueries(text, _entity.Table, predicates);
        }

        if (named)
        {
            var windows = SqlDialects.WindowSpecs(text); // the named windows of the select list and the ordering
            text = text.Replace(WindowsMarker, windows.Count == 0 ? "" : " WINDOW " + string.Join(", ", windows.Select((spec, i) => "w" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " AS (" + spec + ")")));
            text = SqlDialects.NameWindows(text, windows);
        }

        return ForDialect(text, dialect);
    }

    /// <summary>Where a statement's WINDOW clause goes (after HAVING, before ORDER BY), until its windows are known.</summary>
    private const string WindowsMarker = "{windows}";
}
