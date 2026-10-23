using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Explicit loading: a collection or a to-one navigation of rows already in hand (<c>session.LoadAsync(order, o => o.Lines)</c>,
///     <c>session.LoadAsync(orders, o => o.Customer)</c>), one statement for all the rows given (their keys as an IN list, in runs of
///     500), through the registered maps: a collection by the children's foreign key (or the join table of a many-to-many side), with the
///     child's visibility rules as an Include applies them; a navigation by the parents' keys joined to their targets. Nothing is tracked:
///     the property is set, that is all.
/// </summary>
public sealed partial class SqlSession
{
    private const int LoadRun = 500; // keys per statement: under SQL Server's parameter limit, one round trip for the usual page

    public Task LoadAsync<T, TMember>(T entity, Expression<Func<T, TMember>> member, CancellationToken ct = default) where T : class
        => LoadAsync([entity], member, ct);

    public async Task LoadAsync<T, TMember>(IReadOnlyList<T> entities, Expression<Func<T, TMember>> member, CancellationToken ct = default) where T : class
    {
        var name = MemberName(member);
        var map = SqlEntityMap<T>.Current;
        if (entities.Count == 0)
            return;
        var parents = new List<object>(entities.Count);
        foreach (var entity in entities)
            parents.Add(entity);
        if (map.Collection(name) is { } collection)
            await LoadCollectionAsync(map, collection, parents, ct).ConfigureAwait(false);
        else if (map.Navigation(name) is { } navigation)
            await LoadNavigationAsync(map, navigation, parents, ct).ConfigureAwait(false);
        else
            throw new ArgumentException($"'{name}' is not a navigation or a collection of {typeof(T).Name}.", nameof(member));
    }

    /// <summary>The children of the parents in one statement per run of keys, grouped by the parent's key (the child's foreign key, or the join table's column read last) and assigned; an empty list where none.</summary>
    private async Task LoadCollectionAsync(SqlEntityMap map, SqlCollection collection, List<object> parents, CancellationToken ct)
    {
        SqlKeyMatch.Check(map, collection); // a composite parent: one foreign key per key column
        var child = SqlEntityMap.For(collection.Child) ?? throw new InvalidOperationException($"No map is registered for {collection.Child.Name}.");
        var groups = new Dictionary<object, List<object>>();
        foreach (var run in Runs(parents, map))
        {
            var kinds = new List<SqlParamKind>();
            var values = new List<object?>();
            AddKeys(map, run, kinds, values);

            // The predicates after the keys: the child's rules (their values appended), a derived type's rows.
            var joins = new List<string>();
            var innerAliases = new HashSet<string>(StringComparer.Ordinal);
            var predicates = new List<string>();
            foreach (var rule in child.Rules)
            {
                var lambda = rule();
                predicates.Add(new ExpressionTranslator(child, lambda.Parameters[0], Dialect, values, kinds, joins, null, innerAliases).Predicate(lambda.Body));
            }

            if (child.RowPredicate is { } fixedPredicate)
                predicates.Add(fixedPredicate);
            var side = collection.IsLink ? "t_l" : "t";
            var sql = new StringBuilder("SELECT ");
            sql.Append(string.Join(", ", child.Columns.Select(c => c.Select("t"))));
            if (collection.IsLink)
                sql.Append(", ").Append(SqlKeyMatch.List(collection.ForeignKeyColumns, "t_l")); // the parent's key from the join table (every column of a composite one)
            sql.Append(" FROM \"").Append(child.Table).Append("\" t");
            if (collection.IsLink)
                sql.Append(' ').Append(collection.LinkJoin("t", "t_l"));
            foreach (var join in joins)
                sql.Append(' ').Append(SqlNavigation.Inner(join, innerAliases));
            sql.Append(" WHERE (").Append(KeysCondition(side, collection.ForeignKeyColumns, run.Count)).Append(')');
            foreach (var predicate in predicates)
                sql.Append(" AND (").Append(predicate).Append(')');

            using var command = FreshCommand(DynamicPlanner.Finish(sql.ToString(), Dialect, kinds), kinds.ToArray());
            for (var i = 0; i < values.Count; i++)
                command.Parameters[i].Value = SqlValue.ToDatabase(Dialect, values[i]);
            using var reader = await ReadAsync(command, "load \"" + child.Table + "\"", ct).ConfigureAwait(false);
            var keyReaders = KeyColumnsOf(map);
            var keyOrdinal = child.Columns.Length;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = child.ReadBoxed(reader, Dialect);
                var key = (collection.IsLink ? DynamicIncludes.LinkKey(reader, Dialect, keyReaders, keyOrdinal) : collection.ChildKey(row))!;
                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = [];
                group.Add(row);
            }
        }

        foreach (var parent in parents)
            collection.Assign(parent, map.WholeKeyOf(parent) is { } key && groups.TryGetValue(key, out var mine) ? mine : []);
    }

    /// <summary>The targets of the parents' navigation in one statement per run: the parent's key beside its target's columns, joined on the foreign key; null set where the key is null or the target gone.</summary>
    private async Task LoadNavigationAsync(SqlEntityMap map, SqlNavigation navigation, List<object> parents, CancellationToken ct)
    {
        var target = SqlEntityMap.For(navigation.Target) ?? throw new InvalidOperationException($"No map is registered for {navigation.Target.Name}.");
        var keyColumns = KeyColumnsOf(map);
        var targets = new Dictionary<object, object>();
        foreach (var run in Runs(parents, map))
        {
            var kinds = new List<SqlParamKind>();
            var values = new List<object?>();
            AddKeys(map, run, kinds, values);
            // The parent's key columns first (a composite key's all), the target's after, joined on every key column of the target.
            var sql = new StringBuilder("SELECT ").Append(SqlKeyMatch.List(map.KeyColumns, "t")).Append(", ");
            sql.Append(string.Join(", ", target.Columns.Select(c => c.Select("j"))));
            var ruleJoins = new List<string>();
            var filter = ExpressionTranslator.JoinFilter(target, "j", Dialect, values, kinds, ruleJoins, null); // a target the caller cannot see is not loaded: null
            sql.Append(" FROM \"").Append(map.Table).Append("\" t ").Append(navigation.Join(target, "t", "j").Replace("LEFT JOIN", "INNER JOIN")).Append(filter);
            foreach (var join in ruleJoins)
                sql.Append(' ').Append(join);
            sql.Append(" WHERE (").Append(KeysCondition("t", map.KeyColumns, run.Count)).Append(')');
            using var command = FreshCommand(DynamicPlanner.Finish(sql.ToString(), Dialect, kinds), kinds.ToArray());
            for (var i = 0; i < values.Count; i++)
                command.Parameters[i].Value = SqlValue.ToDatabase(Dialect, values[i]);
            using var reader = await ReadAsync(command, "load \"" + target.Table + "\"", ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                targets[ReadKey(reader, keyColumns)!] = navigation.ReadAt(reader, keyColumns.Length, Dialect);
        }

        foreach (var parent in parents)
            navigation.Set(parent, map.WholeKeyOf(parent) is { } key && targets.TryGetValue(key, out var one) ? one : null);
    }

    /// <summary>The parents' distinct keys, in runs of <see cref="LoadRun" />.</summary>
    private static List<List<object>> Runs(List<object> parents, SqlEntityMap map)
    {
        var seen = new HashSet<object>();
        var runs = new List<List<object>>();
        var current = new List<object>();
        foreach (var parent in parents)
        {
            if (map.WholeKeyOf(parent) is not { } key || !seen.Add(key))
                continue;
            if (current.Count == LoadRun)
            {
                runs.Add(current);
                current = [];
            }

            current.Add(key);
        }

        if (current.Count > 0)
            runs.Add(current);
        return runs;
    }

    /// <summary>The parameters of a run of keys, in order: one per key, or a composite key's values one after the other.</summary>
    private static void AddKeys(SqlEntityMap map, List<object> run, List<SqlParamKind> kinds, List<object?> values)
    {
        var keyColumns = KeyColumnsOf(map);
        foreach (var key in run)
        {
            if (key is SqlCompositeKey composite)
            {
                for (var i = 0; i < keyColumns.Length; i++)
                {
                    kinds.Add(keyColumns[i].Kind);
                    values.Add(composite.Values[i]);
                }
            }
            else
            {
                kinds.Add(keyColumns[0].Kind);
                values.Add(key);
            }
        }
    }

    /// <summary><c>alias."K" IN (@p0, …)</c> for one column; one conjunction per key, OR'ed, for several (SQL Server has no row-value IN).</summary>
    private static string KeysCondition(string alias, string[] columns, int count)
    {
        if (columns.Length == 1)
            return $"{alias}.\"{columns[0]}\" IN ({Placeholders(0, count)})";
        var text = new StringBuilder();
        for (var k = 0; k < count; k++)
        {
            text.Append(k > 0 ? " OR (" : "(");
            for (var i = 0; i < columns.Length; i++)
                text.Append(i > 0 ? " AND " : "").Append(alias).Append(".\"").Append(columns[i]).Append("\" = @p").Append((k * columns.Length + i).ToString(CultureInfo.InvariantCulture));
            text.Append(')');
        }

        return text.ToString();
    }

    private static SqlColumn[] KeyColumnsOf(SqlEntityMap map)
        => Array.ConvertAll(map.KeyColumns, k => Array.Find(map.Columns, c => c.Column == k) ?? throw new InvalidOperationException($"\"{map.Table}\" has no column \"{k}\"."));

    /// <summary>The key read at ordinal 0 on: the column's value, or a <see cref="SqlCompositeKey" /> of the key columns.</summary>
    private object? ReadKey(DbDataReader reader, SqlColumn[] keyColumns)
    {
        if (keyColumns.Length == 1)
            return keyColumns[0].Read(reader, 0, Dialect);
        var values = new object?[keyColumns.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = keyColumns[i].Read(reader, i, Dialect);
        return new SqlCompositeKey(values);
    }

    private static string Placeholders(int from, int count)
    {
        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
            text.Append(i > 0 ? ", @p" : "@p").Append((from + i).ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    /// <summary>The property a member lambda names (<c>o => o.Lines</c>, through a cast); nothing else.</summary>
    private static string MemberName(LambdaExpression member)
    {
        var body = member.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.Quote } unary)
            body = unary.Operand;
        if (body is MemberExpression { Expression: ParameterExpression } access)
            return access.Member.Name;
        throw new ArgumentException($"'{member}' is not a member of the lambda parameter: LoadAsync takes o => o.Lines or o => o.Customer.", nameof(member));
    }
}
