namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The keys of a tree's descendants or ancestors along a self-referencing parent key (Pragmatic's <c>[GenerateHierarchy]</c>), as
///     <c>HierarchyCteSql</c> walks them: a recursive CTE over the keys from the starting row, bounded in depth (a cycle between parents ends
///     there), the starting row itself left out. The walk reads no filter, as on EF Core, where the filters apply to the rows the keys select:
///     the generated <c>GetDescendantsBy…</c>/<c>GetAncestorsBy…</c> read those rows with a compiled chain, the caller's filters applied.
/// </summary>
public static class SqlHierarchy
{
    /// <summary>The default bound, Pragmatic's (<c>HierarchyCteSql.Build</c>'s <c>maxDepth</c>).</summary>
    public const int MaxDepth = 100;

    /// <summary>
    ///     The keys reached from <paramref name="start" />: its descendants when <paramref name="descendants" />, its ancestors otherwise.
    ///     <paramref name="parentProperty" /> is the entity's parent key property; table and columns are read from the entity's map.
    /// </summary>
    public static async Task<List<Guid>> KeysAsync<T>(SqlSession session, string parentProperty, Guid start, bool descendants, int maxDepth = MaxDepth, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = SqlEntityMap.For(typeof(T)) ?? throw new InvalidOperationException($"No map is registered for {typeof(T).Name}: it is not an [Entity] of a translated assembly.");
        var parent = map.Columns.FirstOrDefault(c => c.Property == parentProperty)?.Column
                     ?? throw new InvalidOperationException($"{typeof(T).Name} has no column for {parentProperty}.");
        var (table, key) = ($"\"{map.Table}\"", $"\"{map.KeyColumn}\"");
        var recursive = session.Dialect is SqlDialect.SqlServer or SqlDialect.Oracle ? "WITH" : "WITH RECURSIVE";
        // Descendants: a child's parent is a reached key. Ancestors: a reached row's parent is the next key.
        var step = descendants ? $"INNER JOIN h ON n.\"{parent}\" = h.k" : $"INNER JOIN h ON n.{key} = h.p";
        var sql = $"{recursive} h (k, p, d) AS (SELECT {key}, \"{parent}\", 0 FROM {table} WHERE {key} = @p0"
                  + $" UNION ALL SELECT n.{key}, n.\"{parent}\", h.d + 1 FROM {table} n {step} WHERE h.d < {maxDepth})"
                  + " SELECT k FROM h WHERE k <> @p0"
                  + (session.Dialect == SqlDialect.SqlServer ? $" OPTION (MAXRECURSION {maxDepth + 1})" : "");
        using var command = session.FreshCommand(sql, [SqlParamKind.Guid]);
        command.Parameters[0].Value = SqlValue.ToDatabase(session.Dialect, start);
        var keys = new List<Guid>();
        var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                keys.Add(SqlValue.Guid(reader, 0, session.Dialect));
        }

        return keys;
    }
}
