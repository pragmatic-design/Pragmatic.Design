using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     Reads a runtime chain down to its root and turns it into a plan, with the generator's rules. Two caches:
///     by shape (<see cref="ShapeReader" />: the second call with the same tree shape skips the translation and
///     reads only the captured leaves) and by text (two shapes with the same SQL share one plan, so one prepared
///     command).
/// </summary>
internal static partial class DynamicPlanner
{
    private const int CacheLimit = 1024;
    private static readonly ConcurrentDictionary<(SqlDialect, string, Type), object> s_plans = new();

    public static DynamicTranslation<TResult> Plan<TResult>(SqlSession session, Expression expression, string terminal, LambdaExpression? argument)
    {
        // The chain from the root out: the operators in the order they were applied.
        var steps = new List<MethodCallExpression>();
        var node = expression;
        while (node is MethodCallExpression call)
        {
            steps.Add(call);
            node = call.Arguments[0];
        }

        steps.Reverse();
        if (node is not ConstantExpression { Value: ISqlRoot root })
            throw new NotSupportedException("The chain does not start from a SqlSession.Query<T>(): a queryable of another provider cannot be translated.");
        var map = SqlEntityMap.For(root.EntityType) ?? throw new InvalidOperationException(root.Raw is null
            ? $"No map is registered for {root.EntityType.Name}."
            : $"A hand-written statement on the runtime path reads an [Entity] ({root.EntityType.Name} is none): a [MapFrom] DTO as its rows needs the typed chain, where the generator reads the DTO.");
        var dialect = session.Dialect;

        // A hand-written root: its values are the first parameters, its text the FROM; the chain's shape is not cached (the values live in the root, not in the tree).
        var raw = root.Raw;
        var shape = ShapeReader.Rent(map);
        var supported = raw is null && shape.Read(steps, dialect, terminal, argument);
        if (supported && ShapeCache<TResult>.Lookup.TryGetValue(shape.Key, out var known))
            return new DynamicTranslation<TResult>(known.Plan, known.Values(shape.Leaves));

        // First time for this shape: translate, and record where each parameter's value came from.
        var sources = new List<CaptureSource>();
        var cacheable = supported;
        Dictionary<Expression, int>? leafIndex = null;
        if (cacheable)
        {
            leafIndex = new Dictionary<Expression, int>(shape.Leaves.Count, ReferenceEqualityComparer.Instance);
            for (var i = 0; i < shape.Leaves.Count; i++)
                leafIndex[shape.Leaves[i]] = i;
        }

        object? Capture(Expression? captured, SqlCaptureKind kind, string prefix, string suffix, Func<object?, object?>? convert)
        {
            var leaf = -1;
            if (kind != SqlCaptureKind.True && (leafIndex is null || !leafIndex.TryGetValue(captured!, out leaf)))
            {
                if (captured is ConstantExpression)
                {
                    // A literal the shape reader did not list as a leaf (a collection include's filter): the same value every call, kept with the shape.
                    var constant = CaptureSource.Direct(captured, kind, prefix, suffix, convert);
                    sources.Add(new CaptureSource(-1, SqlCaptureKind.Constant, "", "", constant));
                    return constant;
                }

                cacheable = false; // the reader and the translator disagreed on this node: translated every time, as before
            }

            sources.Add(new CaptureSource(leaf, kind, prefix, suffix, null, convert));
            return CaptureSource.Direct(captured, kind, prefix, suffix, convert);
        }

        var values = new List<object?>();
        var kinds = new List<SqlParamKind>();
        if (raw is not null)
        {
            foreach (var value in raw.Values)
            {
                values.Add(value);
                kinds.Add(SqlValue.KindOf(value));
            }
        }

        var joins = new List<string>();
        var innerAliases = new HashSet<string>(StringComparer.Ordinal); // optional navigations the predicates need a row for
        var predicates = new List<string>();
        var orderings = new List<string>();
        var orderKeys = new List<(string Sql, bool Descending, SqlColumn? Column)>(); // the ordering's keys, for a cursor
        string? keyset = null; // the keyset predicate, after the rules (the generator's order)
        var reversed = false; // Before: the ordering flipped in the statement, the rows flipped back once read
        var includes = new List<RuntimeInclude>();
        string? skip = null, take = null;
        string? selectList = null;
        Func<DbDataReader, SqlDialect, TResult>? reader = null;
        SqlLoad<TResult>? load = null;
        var companions = new List<string>(); // the collection includes' statements, after the rows' own in the same command
        var projected = false;
        var distinct = false;
        SqlProjectionMap? declared = null; // the registered projection of a Select or Project into a [MapFrom] DTO

        string Predicate(LambdaExpression lambda, SqlEntityMap on)
        {
            var translator = new ExpressionTranslator(on, lambda.Parameters[0], dialect, values, kinds, joins, Capture, innerAliases);
            return translator.Predicate(lambda.Body);
        }

        string Column(LambdaExpression lambda, out SqlColumn? column)
        {
            var translator = new ExpressionTranslator(map, lambda.Parameters[0], dialect, values, kinds, joins, Capture);
            var text = translator.Column(lambda.Body);
            column = translator.LastColumn;
            return text;
        }

        void Order(LambdaExpression lambda, bool descending, bool reset)
        {
            var text = Column(lambda, out var column);
            if (reset)
            {
                orderings.Clear();
                orderKeys.Clear();
            }

            orderings.Add(text + (descending ? " DESC" : ""));
            orderKeys.Add((text, descending, column));
        }

        string Parameter(object? value)
        {
            values.Add(value);
            kinds.Add(SqlValue.KindOf(value));
            return "{" + (values.Count - 1).ToString(CultureInfo.InvariantCulture) + "}";
        }

        foreach (var step in steps)
        {
            var name = step.Method.Name;
            if (projected && name is not ("Skip" or "Take" or "Distinct"))
                throw new NotSupportedException($"'{name}' after a Select is not translated: put the filter and the ordering before the projection.");
            switch (name)
            {
                case "Distinct":
                    distinct = true;
                    break;
                case "Union" or "Concat" or "Except" or "Intersect" or "Join" or "LeftJoin":
                    throw new NotSupportedException($"'{name}' is translated at compile time only: write the chain on SqlQuery<T> (var), with the other chain in place.");
                case "OfType":
                    throw new NotSupportedException("'OfType' is translated at compile time only: at runtime start the chain from Query<TDerived>() instead.");
                case "DistinctOn":
                    throw new NotSupportedException("'DistinctOn' is translated at compile time only: write the chain on SqlQuery<T> (var).");
                case "Where":
                    predicates.Add(Predicate(Lambda(step.Arguments[1]), map));
                    break;
                case "OrderBy":
                case "OrderByDescending":
                    Order(Lambda(step.Arguments[1]), name.EndsWith("Descending", StringComparison.Ordinal), reset: true);
                    break;
                case "ThenBy":
                case "ThenByDescending":
                    Order(Lambda(step.Arguments[1]), name.EndsWith("Descending", StringComparison.Ordinal), reset: false);
                    break;
                case "After":
                case "Before":
                {
                    // The cursor's items, one parameter per ordering key, through the key column's converter; the predicate goes after the rules.
                    // Before compares the other way and flips the ORDER BY: the rows nearest before the cursor come first, the plan turns them back.
                    if (orderKeys.Count == 0)
                        throw new NotSupportedException($"{name} continues an ordering: put OrderBy/ThenBy before it.");
                    reversed = name == "Before";
                    if (reversed)
                    {
                        for (var i = 0; i < orderings.Count; i++)
                            orderings[i] = orderKeys[i].Sql + (orderKeys[i].Descending ? "" : " DESC");
                    }

                    var count = orderKeys.Count;
                    var placeholders = new string[count];
                    for (var i = 0; i < count; i++)
                    {
                        var index = i;
                        var toStore = orderKeys[i].Column?.ToStore;
                        var value = Capture(step.Arguments[1], SqlCaptureKind.Value, "", "", cursor => toStore is null ? SqlKeyset.Item(cursor, index, count) : toStore(SqlKeyset.Item(cursor, index, count)));
                        values.Add(value);
                        kinds.Add(orderKeys[i].Column?.Kind ?? SqlValue.KindOf(value));
                        placeholders[i] = "{" + (values.Count - 1).ToString(CultureInfo.InvariantCulture) + "}";
                    }

                    keyset = SqlKeyset.Predicate(dialect, orderKeys.Select(k => k.Sql).ToList(), orderKeys.Select(k => k.Descending != reversed).ToList(), placeholders);
                    break;
                }
                case "Skip":
                    skip = Parameter(Capture(step.Arguments[1], SqlCaptureKind.Value, "", "", null));
                    break;
                case "Take":
                    take = Parameter(Capture(step.Arguments[1], SqlCaptureKind.Value, "", "", null));
                    break;
                case "AsNoTracking":
                    break; // nothing tracks here
                case "Include":
                    DynamicIncludes.Add(includes, map, Lambda(step.Arguments[1]));
                    break;
                case "Select":
                {
                    if (includes.Count > 0)
                        throw new NotSupportedException("Include works on the entity, not with a projection: project the navigation's columns instead.");
                    var lambda = Lambda(step.Arguments[1]);
                    (selectList, reader, declared) = Projection<TResult>(map, lambda, dialect, values, kinds, joins, Capture);
                    projected = true;
                    break;
                }

                case "Project":
                {
                    if (includes.Count > 0)
                        throw new NotSupportedException("Include works on the entity, not with a projection: project the navigation's columns instead.");
                    (selectList, reader, declared) = Declared<TResult>(map, dialect, values, kinds, joins, Capture);
                    projected = true;
                    break;
                }

                default:
                    throw new NotSupportedException($"'{name}' is not in the translated subset (Where, OrderBy/ThenBy, Skip, Take, Select, and the async terminals).");
            }
        }

        // The entity's rules, every time: a runtime chain cannot opt out (WithoutFilters is an operator of the typed chain). The
        // shape reader built the trees of this call; translating those same objects is what lets their leaves be found.
        for (var i = 0; i < map.Rules.Length; i++)
            predicates.Add(Predicate(i < shape.Rules.Count ? shape.Rules[i] : map.Rules[i](), map));
        if (map.RowPredicate is { } rowPredicate)
            predicates.Add(rowPredicate); // a derived type's rows and a [SoftDelete] entity's live ones, as the generator adds them to every plan

        var scalar = terminal is "CountAsync" or "AnyAsync" or "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync";
        if (distinct && terminal is "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync")
            throw new NotSupportedException("Distinct does not apply to the scalar aggregates (SUM, MIN, MAX, AVG read the rows).");
        if (argument is not null && terminal is "FirstOrDefaultAsync" or "CountAsync" or "AnyAsync")
            predicates.Add(Predicate(argument, map));
        if (keyset is not null)
            predicates.Add(keyset);
        var limit = terminal switch
        {
            "FirstOrDefaultAsync" or "FirstAsync" or "AnyAsync" => "1",
            "SingleAsync" or "SingleOrDefaultAsync" => "2",
            _ => take,
        };
        if (terminal is "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync")
        {
            var column = Column(argument ?? throw new ArgumentNullException(nameof(argument)), out _);
            var function = terminal switch { "SumAsync" => "SUM", "MinAsync" => "MIN", "MaxAsync" => "MAX", _ => "AVG" };
            selectList = $"{function}({column})";
            var valueColumn = ColumnFor(map, argument);
            reader = terminal == "AverageAsync"
                ? (r, d) => (TResult)(object)SqlValue.Average(r, 0, d)
                : (r, d) => (TResult)valueColumn.Read(r, 0, d)!;
        }
        else if (terminal == "CountAsync" && distinct)
        {
            // The distinct rows counted through a derived table, as the generator does: COUNT(*) over SELECT DISTINCT the columns.
            var rows = projected ? selectList! : string.Join(", ", map.Columns.Select(c => c.Select("t")));
            var columnsAliased = string.Join(", ", rows.Split(", ").Select((c, i) => $"{c} AS c{i}"));
            distinct = false;
            var inner = Render(columnsAliased);
            selectList = "COUNT(*)";
            var countText = Finish($"SELECT COUNT(*) FROM (SELECT DISTINCT {inner.Substring("SELECT ".Length)}) t", dialect, kinds);
            var countPlan = TextPlan<TResult>(dialect, countText, countText, kinds, null, null);
            if (cacheable)
                ShapeCache<TResult>.Add(shape.Key.ToString(), new ShapeEntry<TResult>(countPlan, sources.ToArray()));
            return new DynamicTranslation<TResult>(countPlan, values.ToArray());
        }
        else if (terminal == "CountAsync")
            selectList = "COUNT(*)";
        else if (terminal == "AnyAsync")
            selectList = "1";
        else if (!projected)
        {
            // The entity's columns, then those of the to-one includes (their joins after the predicates', as the generator orders them).
            var columns = map.Columns.Select(c => c.Select("t")).ToList();
            DynamicIncludes.Joins(includes, "t", joins, columns, (target, alias) => ExpressionTranslator.JoinFilter(target, alias, dialect, values, kinds, joins, Capture));
            selectList = string.Join(", ", columns);
            reader = DynamicIncludes.Reader(SqlEntityMap<TResult>.Current.Read, includes, map.Columns.Length);
            load = DynamicIncludes.Loader<TResult>(includes, map, dialect, s => Render(s, keysOnly: true), companions, values, kinds, Capture);
            if (load is not null && terminal == "AsAsyncEnumerable")
                throw new NotSupportedException("AsAsyncEnumerable streams the rows one at a time: a collection Include needs them together; use ToListAsync.");
        }
        else if (includes.Count > 0)
        {
            DynamicIncludes.Joins(includes, "t", joins, [], (target, alias) => ExpressionTranslator.JoinFilter(target, alias, dialect, values, kinds, joins, Capture)); // a count with an Include keeps the joins, as the typed chain does
        }
        else if (declared is { Collections.Length: > 0 } && !scalar)
        {
            // The DTO's collection members: companion statements on the parent key the DTO reads, as the generator emits them.
            load = DynamicProjections.Loader<TResult>(declared, map, dialect, s => Render(s, keysOnly: true), companions, values, kinds, Capture);
            if (terminal == "AsAsyncEnumerable")
                throw new NotSupportedException("AsAsyncEnumerable streams the rows one at a time: a DTO with a collection needs them together; use ToListAsync.");
        }

        // The rows' statement with a given select list: its own, or a parent key for a companion statement's subquery (keysOnly: the
        // ordering only with the paging it bounds; alone it means nothing in an IN list, and SQL Server refuses it there).
        string Render(string select, bool keysOnly = false)
        {
            var sql = new StringBuilder();
            sql.Append("SELECT ");
            if (distinct && terminal != "AnyAsync")
                sql.Append("DISTINCT ");
            sql.Append(select);
            if (raw is null)
                sql.Append(" FROM \"").Append(map.Table).Append("\" t");
            else
                sql.Append(" FROM (").Append(raw.Text).Append(") t"); // the statement as written: its identifiers double-quoted like the fragments', its @pN the first parameters
            foreach (var join in joins)
                sql.Append(' ').Append(SqlNavigation.Inner(join, innerAliases));
            for (var i = 0; i < predicates.Count; i++)
                sql.Append(i == 0 ? " WHERE (" : " AND (").Append(predicates[i]).Append(')');
            if (terminal != "CountAsync" && terminal is not ("SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync"))
            {
                if (orderings.Count > 0 && !scalar && (!keysOnly || skip is not null || limit is not null))
                    sql.Append(" ORDER BY ").Append(string.Join(", ", orderings));
                else if ((skip is not null || limit is not null) && dialect == SqlDialect.SqlServer)
                    sql.Append(" ORDER BY (SELECT NULL)");
                sql.Append(SqlDialects.Paging(dialect, skip, limit));
            }

            // A RANGE frame with an offset, in the select list or the ordering: SQL Server's correlated subquery under these predicates.
            return dialect == SqlDialect.SqlServer && raw is null ? SqlDialects.RangeSubqueries(sql.ToString(), map.Table, predicates) : sql.ToString();
        }

        var command = new StringBuilder(Render(selectList!));
        foreach (var companion in companions)
            command.Append("; ").Append(companion);
        var text = Finish(command.ToString(), dialect, kinds);
        // Collection includes leave the text alone, so they are part of the plan's identity.
        var plan = TextPlan(dialect, text, includes.Count == 0 ? text : text + "\n" + DynamicIncludes.Signature(includes), kinds, reader, load, reversed);
        if (raw is not null)
            return new DynamicTranslation<TResult>(plan, values.ToArray());
        if (cacheable)
            ShapeCache<TResult>.Add(shape.Key.ToString(), new ShapeEntry<TResult>(plan, sources.ToArray()));
        return new DynamicTranslation<TResult>(plan, values.ToArray());
    }
}
