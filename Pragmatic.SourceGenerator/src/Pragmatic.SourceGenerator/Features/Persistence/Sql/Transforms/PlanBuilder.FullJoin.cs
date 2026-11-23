using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A FULL JOIN on MySQL, which has none: the statement twice, once with a LEFT JOIN (every outer row, matched or not) and once with a RIGHT
///     JOIN keeping only the rows without an outer row (the inner rows that matched nothing), as <c>UNION ALL</c>. Each half selects the row's
///     values as <c>cN</c> and the orderings' values as <c>oN</c>; the wrapper reads the values back, counts them or aggregates them, and orders
///     and pages the union. The halves are the same text but for the join and that one predicate, so the filters, the rules and their parameters
///     are the statement's own.
/// </summary>
internal sealed partial class PlanBuilder
{
    private int _fullJoinAt = -1; // the FULL JOIN's place in _joins; -1 without one
    private string? _outerMissing; // the predicate true on a row the FULL JOIN left without an outer row
    private bool _renderingHalf;

    /// <summary>Records the FULL JOIN about to be added, for the MySQL form.</summary>
    private void MarkFullJoin()
    {
        _fullJoinAt = _joins.Count;
        _outerMissing = $"{_alias}.\"{_entity.KeyColumn}\" IS NULL";
        TerminalEmulations.Add(("FullJoin", SqlDialect.MySql, "MySQL has no FULL JOIN: the rows are the LEFT JOIN's, then the RIGHT JOIN's without an outer row, as UNION ALL"));
    }

    /// <summary>True when this statement is the MySQL form of a FULL JOIN, rendered by <see cref="FullJoinOnMySql" /> instead.</summary>
    private bool RendersFullJoinOnMySql(SqlDialect dialect) => dialect == SqlDialect.MySql && _fullJoinAt >= 0 && !_renderingHalf && _parent is null;

    private string FullJoinOnMySql(string selectList)
    {
        var counted = _terminal is "CountAsync" or "AnyAsync";
        List<string> values = counted ? ["1"] : IsAggregate ? [_aggregateColumn!] : MemberColumns();
        var orderings = counted || IsAggregate ? [] : _orderings.Select(Direction).ToList();
        var halfList = string.Join(", ", values.Select((v, i) => $"{v} AS c{i}").Concat(orderings.Select((o, i) => $"{o.Value} AS o{i}")));
        var wrapperList = counted ? selectList : IsAggregate ? selectList.Replace(_aggregateColumn!, "fj.c0") : string.Join(", ", values.Select((_, i) => $"fj.c{i}"));

        var full = _joins[_fullJoinAt];
        var orderingsBefore = _orderings.ToList();
        var (skip, limit, distinct, cteHoisted) = (_skipParameter, _limit, _distinct, _cteHoisted);
        _renderingHalf = true;
        _orderings.Clear();
        _skipParameter = null;
        _limit = null;
        _distinct = false;
        _cteHoisted = true; // a named query of the inner chain goes once, before the wrapper
        string matched, unmatched;
        try
        {
            _joins[_fullJoinAt] = "LEFT" + full.Substring("FULL".Length);
            matched = Statement(SqlDialect.MySql, halfList);
            _joins[_fullJoinAt] = "RIGHT" + full.Substring("FULL".Length);
            _predicates.Add(_outerMissing!);
            unmatched = Statement(SqlDialect.MySql, halfList);
        }
        finally
        {
            _predicates.Remove(_outerMissing!);
            _joins[_fullJoinAt] = full;
            _renderingHalf = false;
            _orderings.AddRange(orderingsBefore);
            (_skipParameter, _limit, _distinct, _cteHoisted) = (skip, limit, distinct, cteHoisted);
        }

        var sql = new StringBuilder();
        if (_hoisted is not null && !_cteHoisted)
            sql.Append(ForDialect(Resolve(WithClause(SqlDialect.MySql), SqlDialect.MySql), SqlDialect.MySql));
        sql.Append("SELECT ").Append(_distinct ? "DISTINCT " : "").Append(wrapperList).Append(" FROM (").Append(matched).Append(" UNION ALL ").Append(unmatched).Append(") fj");
        if (!(counted || IsAggregate))
        {
            if (orderings.Count > 0)
                sql.Append(" ORDER BY ").Append(string.Join(", ", orderings.Select((o, i) => $"fj.o{i}" + (o.Descending ? " DESC" : ""))));
            sql.Append(SqlDialects.Paging(SqlDialect.MySql, _skipParameter, _limit));
        }

        return sql.ToString(); // the halves are resolved already; the wrapper adds only its own aliases and the paging's parameters

        static (string Value, bool Descending) Direction(string ordering)
            => ordering.EndsWith(" DESC", StringComparison.Ordinal) ? (ordering.Substring(0, ordering.Length - " DESC".Length), true) : (ordering, false);
    }
}
