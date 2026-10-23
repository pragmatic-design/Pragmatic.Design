using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The runtime twin of the generator's paths: member chains on the parameter to any depth (joins under nested aliases,
///     owned members, a navigation as its key, a collection's Count), and the collection predicates <c>Any</c>, <c>All</c>,
///     <c>Count</c> as correlated subqueries with a nested translator over the child.
/// </summary>
internal sealed partial class ExpressionTranslator
{
    private readonly string _alias = "t";
    private readonly int _depth;

    /// <summary>A nested translator over a child map inside a correlated subquery: its own alias and joins, the parent's values.</summary>
    private ExpressionTranslator(ExpressionTranslator parent, SqlEntityMap child, ParameterExpression parameter, string alias)
    {
        _map = child;
        _parameter = parameter;
        _dialect = parent._dialect;
        _values = parent._values;
        _kinds = parent._kinds;
        _joins = [];
        _capture = parent._capture;
        _innerAliases = null;
        _alias = alias;
        _depth = parent._depth + 1;
    }

    /// <summary>A navigation's join, once per alias, its target's filter in the ON clause (see <see cref="JoinFilter" />).</summary>
    private void AddJoin(string join, SqlEntityMap target, string alias)
    {
        if (_joins.Exists(j => j == join || j.StartsWith(join + " AND ", StringComparison.Ordinal)))
            return; // the same navigation, read again
        var at = _joins.Count;
        _joins.Add(join); // before the filter: a rule's own navigations join after it
        _joins[at] = join + JoinFilter(target, alias, _dialect, _values, _kinds, _joins, _capture);
    }

    /// <summary>
    ///     The target's rules on a navigation's join, as the generator writes them and EF Core applies its query filters there: the derived type's
    ///     rows, a [SoftDelete] target's live ones, its [VisibleWhen] rules under the join's alias, as <c> AND (…)</c>; empty when the target
    ///     filters nothing. A rule's value read from <see cref="SqlContext" /> is not a leaf of the chain's shape: such a chain is translated each
    ///     call (the planner's capture finds no leaf for it), so the value is the caller's.
    /// </summary>
    internal static string JoinFilter(SqlEntityMap target, string alias, SqlDialect dialect, List<object?> values, List<SqlParamKind> kinds, List<string> joins, SqlCapture? capture)
    {
        var parts = new List<string>();
        if (target.RowPredicate is { } rowPredicate)
            parts.Add(rowPredicate.Replace("t.\"", alias + ".\""));
        foreach (var rule in target.Rules)
        {
            var lambda = rule();
            parts.Add(new ExpressionTranslator(target, lambda.Parameters[0], dialect, values, kinds, joins, capture, alias: alias).Predicate(lambda.Body));
        }

        return parts.Count == 0 ? "" : " AND " + string.Join(" AND ", parts.Select(p => $"({p})"));
    }

    /// <summary>
    ///     A declared DTO's join (one hop from <paramref name="owner" />'s rows under <c>t</c>, the navigation's alias <c>j_Nav</c>) with its target's
    ///     filter; the join as it is when it is not one of the owner's navigations.
    /// </summary>
    internal static string DeclaredJoin(SqlEntityMap owner, string join, SqlDialect dialect, List<object?> values, List<SqlParamKind> kinds, List<string> joins, SqlCapture? capture)
    {
        foreach (var navigation in owner.Navigations)
        {
            if (SqlEntityMap.For(navigation.Target) is not { } target)
                continue;
            var alias = AliasOf("t", navigation.Property);
            if (navigation.Join(target, "t", alias) == join)
                return join + JoinFilter(target, alias, dialect, values, kinds, joins, capture);
        }

        return join;
    }

    private static string AliasOf(string parentAlias, string property) => parentAlias == "t" ? "j_" + property : parentAlias + "_" + property;

    /// <summary>The member names from the parameter outwards; null when the chain is not rooted in the parameter.</summary>
    private List<string>? Segments(Expression node)
    {
        var segments = new List<string>();
        node = Strip(node);
        while (node is MemberExpression member && member.Expression is not null)
        {
            segments.Insert(0, member.Member.Name);
            node = Strip(member.Expression);
        }

        return node == _parameter ? segments : null;
    }

    private string ColumnOf(MemberExpression member)
    {
        if (_jsonElement is not null)
            return JsonMember(member, _alias, _jsonElement, 0); // inside a JSON array's Any: the path below the element
        var segments = Segments(member) ?? throw Unsupported(member, "not a member of the lambda parameter");
        var map = _map;
        var alias = _alias;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (map.Column(segments[i]) is { IsJson: true } json)
                return JsonMember(member, alias, json, i + 1); // a member path into a JSON column: the value at the path, or an array's Count

            if (i == segments.Count - 2 && map.Column($"{segments[i]}.{segments[i + 1]}") is { } flattened)
            {
                LastColumn = flattened;
                return $"{alias}.\"{flattened.Column}\""; // an owned type's member: a column of this table, no join
            }

            if (i == segments.Count - 2 && segments[i + 1] is "Length" or "Count" && map.Column(segments[i]) is { Kind: SqlParamKind.Array } array)
                return ArrayColumnLength(alias, array);

            if (i == segments.Count - 2 && segments[i + 1] == "Count" && map.Collection(segments[i]) is { } counted)
                return CollectionCount(map, alias, counted, member);

            var navigation = map.Navigation(segments[i]) ?? throw Unsupported(member, $"'{segments[i]}' is not a navigation of {map.Type.Name}");
            var target = SqlEntityMap.For(navigation.Target) ?? throw Unsupported(member, $"no map is registered for {navigation.Target.Name}");
            var next = AliasOf(alias, segments[i]); // a composite target: the join matches every key column
            AddJoin(navigation.Join(target, alias, next), target, next);
            if (_orDepth == 0 && _nullTest != NullTest.IsNull)
                _innerAliases?.Add(next); // a comparison on the joined row (NULL compares false): the row must be there
            map = target;
            alias = next;
        }

        var last = segments[segments.Count - 1];
        if (map.Column(last) is { } column)
        {
            LastColumn = column;
            return $"{alias}.\"{column.Column}\"";
        }

        if (map.Navigation(last) is { } asKey)
        {
            // A navigation on its own stands for its foreign key: `o.Customer != null`; asked IS NOT NULL under ANDs, its join can be INNER.
            if (_orDepth == 0 && _nullTest == NullTest.IsNotNull)
                _innerAliases?.Add(AliasOf(alias, last));
            return $"{alias}.\"{asKey.ForeignKeyColumn}\"";
        }

        throw Unsupported(member, $"'{last}' is not a mapped column of {map.Type.Name}");
    }

    /// <summary>The collection a receiver names: the owner map, its alias and the collection; null when it is not one.</summary>
    private (SqlEntityMap Owner, string Alias, SqlCollection Collection)? CollectionOf(Expression receiver)
    {
        if (Segments(receiver) is not { Count: > 0 } segments)
            return null;
        var map = _map;
        var alias = _alias;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var navigation = map.Navigation(segments[i]);
            var target = navigation is null ? null : SqlEntityMap.For(navigation.Target);
            if (navigation is null || target is null)
                return null;
            var next = AliasOf(alias, segments[i]);
            AddJoin(navigation.Join(target, alias, next), target, next);
            map = target;
            alias = next;
        }

        return map.Collection(segments[segments.Count - 1]) is { } collection ? (map, alias, collection) : null;
    }

    /// <summary>The correlation of a child subquery to its parent row: <c>c1."FK" = t."Key"</c>; through a join table, the join of it under <c>c1_l</c> (<paramref name="linkJoin" />) and the correlation on its column.</summary>
    private static string Correlation(SqlEntityMap owner, string ownerAlias, SqlCollection collection, SqlEntityMap child, string childAlias, Expression where, out string? linkJoin)
    {
        if (owner.HasCompositeKey && collection.ForeignKeyColumns.Length != owner.KeyColumns.Length)
            throw Unsupported(where, $"{owner.Type.Name} has a composite key: {collection.Child.Name}'s side carries one column for it, not one per key column");
        linkJoin = null;
        var side = childAlias;
        if (collection.IsLink)
        {
            side = childAlias + "_l"; // a composite child: the link joined on every key column
            linkJoin = collection.LinkJoin(childAlias, side);
        }

        var text = SqlKeyMatch.Matches(collection.ForeignKeyColumns, side, owner, ownerAlias); // every key column of a composite parent
        if (child.RowPredicate is { } fixedPredicate)
            text += " AND " + fixedPredicate.Replace("t.\"", childAlias + ".\"");
        return text;
    }

    private string CollectionCount(SqlEntityMap owner, string ownerAlias, SqlCollection collection, Expression where)
    {
        var child = SqlEntityMap.For(collection.Child) ?? throw Unsupported(where, $"no map is registered for {collection.Child.Name}");
        var alias = "c" + (_depth + 1);
        LastColumn = null;
        var correlation = Correlation(owner, ownerAlias, collection, child, alias, where, out var linkJoin);
        return $"(SELECT COUNT(*) FROM \"{child.Table}\" {alias}{(linkJoin is null ? "" : " " + linkJoin)} WHERE {correlation})";
    }

    /// <summary><c>Any</c>, <c>All</c>, <c>Count</c> on a collection of the row, as the generator renders them.</summary>
    private string CollectionPredicate(MethodCallExpression call)
    {
        var receiver = call.Arguments[0];
        var located = CollectionOf(receiver) ?? throw Unsupported(call, $"'{receiver}' is not a collection of the row ({call.Method.Name} works on a collection navigation)");
        var (owner, ownerAlias, collection) = located;
        var child = SqlEntityMap.For(collection.Child) ?? throw Unsupported(call, $"no map is registered for {collection.Child.Name}");
        var alias = "c" + (_depth + 1);
        var correlation = Correlation(owner, ownerAlias, collection, child, alias, call, out var linkJoin);
        string? inner = null;
        var joins = new List<string>();
        if (call.Arguments.Count > 1)
        {
            var lambda = Strip(call.Arguments[1]) as LambdaExpression ?? throw Unsupported(call, "the predicate must be a lambda");
            var nested = new ExpressionTranslator(this, child, lambda.Parameters[0], alias);
            inner = nested.Translate(lambda.Body, asPredicate: true);
            joins = nested._joins;
        }

        LastColumn = null;
        var from = new StringBuilder().Append("FROM \"").Append(child.Table).Append("\" ").Append(alias);
        if (linkJoin is not null)
            from.Append(' ').Append(linkJoin);
        foreach (var join in joins)
            from.Append(' ').Append(join);
        return call.Method.Name switch
        {
            "Any" => $"EXISTS (SELECT 1 {from} WHERE {correlation}{(inner is null ? "" : $" AND ({inner})")})",
            "All" => inner is null ? throw Unsupported(call, "All takes a predicate") : $"NOT EXISTS (SELECT 1 {from} WHERE {correlation} AND NOT ({inner}))",
            _ => $"(SELECT COUNT(*) {from} WHERE {correlation}{(inner is null ? "" : $" AND ({inner})")})",
        };
    }
}
