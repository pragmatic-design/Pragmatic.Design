using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Builds provider-aware recursive-CTE SQL for hierarchy queries.
/// </summary>
/// <remarks>
///     <para>
///         SQL dialects diverge on recursive CTEs: PostgreSQL and SQLite require the
///         <c>RECURSIVE</c> keyword and use double-quote identifier delimiters, whereas SQL Server
///         rejects <c>WITH RECURSIVE</c> and uses bracket delimiters. Hard-coding either dialect
///         breaks the feature on the other providers, so the dialect is selected at runtime from
///         <see cref="DatabaseFacade.ProviderName"/>.
///     </para>
///     <para>
///         Identifiers come from entity metadata (never end-user input) and are delimited per
///         provider; the root id is always passed as a bound parameter (<c>{0}</c>).
///     </para>
/// </remarks>
public static class HierarchyCteSql
{
    /// <summary>The direction a hierarchy CTE walks the parent foreign key.</summary>
    public enum Direction
    {
        /// <summary>Walk from the root down to all descendants.</summary>
        Descendants,

        /// <summary>Walk from a child up to all ancestors.</summary>
        Ancestors
    }

    /// <summary>
    ///     Builds a recursive-CTE statement that resolves a subtree's rows for the current provider.
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade"/> (used only to read the provider name).</param>
    /// <param name="tableName">The hierarchy table name (from entity metadata).</param>
    /// <param name="idColumn">The primary-key column name.</param>
    /// <param name="parentColumn">The parent foreign-key column name.</param>
    /// <param name="direction">Whether to resolve descendants or ancestors.</param>
    /// <param name="selectColumns">
    ///     Columns to project from the CTE (e.g. <c>"*"</c> for whole rows, or the id column for an id list).
    /// </param>
    /// <param name="excludeRoot">When true, appends a predicate excluding the seed row itself.</param>
    /// <param name="valueAlias">
    ///     When set, the final projection becomes <c>SELECT {selectColumns} AS {valueAlias}</c>
    ///     (e.g. <c>"Value"</c> for <c>SqlQueryRaw&lt;TKey&gt;</c> scalar materialization).
    /// </param>
    /// <param name="maxDepth">
    ///     Maximum recursion depth. Bounds the recursive CTE so a cyclic parent foreign key cannot loop
    ///     forever. SQL Server also gets an explicit <c>OPTION (MAXRECURSION)</c>; PostgreSQL and
    ///     SQLite have no built-in cap, so a depth column inside the CTE bounds them on every
    ///     projection. For <c>"*"</c> the CTE walks keys only and the final statement selects the rows
    ///     from the table, so the depth column never reaches the materialized result.
    /// </param>
    public static string Build(
        DatabaseFacade database,
        string tableName,
        string idColumn,
        string parentColumn,
        Direction direction,
        string selectColumns = "*",
        bool excludeRoot = false,
        string? valueAlias = null,
        int maxDepth = 100)
    {
        var provider = Detect(database.ProviderName);
        var withKeyword = provider == SqlProvider.SqlServer ? "WITH" : "WITH RECURSIVE";
        var table = Quote(tableName, provider);
        var id = Quote(idColumn, provider);
        var parent = Quote(parentColumn, provider);

        // Quote a single-column projection too, as WHERE does: an unquoted reserved-word column in
        // SELECT produces invalid SQL.
        var star = selectColumns == "*";
        var proj = star ? "*" : Quote(selectColumns, provider);

        // Descendants: join child.parent = hierarchy.id. Ancestors: join hierarchy.parent = next.id.
        var recursiveJoin = direction == Direction.Descendants
            ? $"INNER JOIN Hierarchy h ON n.{parent} = h.{id}"
            : $"INNER JOIN Hierarchy h ON h.{parent} = n.{id}";

        // A depth column bounds the recursion on every provider, "*" included: the generated
        // hierarchy queries project "*", and on PostgreSQL and SQLite a cycle between parents is a
        // recursion with no end without the bound. The CTE walks keys only, depth included, and the
        // entity rows come from the table by key, so the depth column never leaks into the
        // materialized result.
        var recursiveWhere = $" WHERE h.__hdepth < {maxDepth}";

        string sql;
        if (star)
        {
            var rootPredicate = excludeRoot ? $" AND {id} <> {{0}}" : string.Empty;
            sql =
                $"{withKeyword} Hierarchy AS (" +
                $" SELECT {id}, {parent}, 0 AS __hdepth FROM {table} WHERE {id} = {{0}}" +
                $" UNION ALL" +
                $" SELECT n.{id}, n.{parent}, h.__hdepth + 1 FROM {table} n {recursiveJoin}{recursiveWhere}" +
                $") SELECT * FROM {table} WHERE {id} IN (SELECT {id} FROM Hierarchy){rootPredicate}";
        }
        else
        {
            var tail = excludeRoot
                ? $" WHERE {id} <> {{0}}"
                : string.Empty;

            var finalProjection = valueAlias is null
                ? proj
                : $"{proj} AS \"{valueAlias.Replace("\"", "\"\"")}\"";

            sql =
                $"{withKeyword} Hierarchy AS (" +
                $" SELECT {proj}, 0 AS __hdepth FROM {table} WHERE {id} = {{0}}" +
                $" UNION ALL" +
                $" SELECT {PrefixColumns(proj, "n")}, h.__hdepth + 1 FROM {table} n {recursiveJoin}{recursiveWhere}" +
                $") SELECT {finalProjection} FROM Hierarchy{tail}";
        }

        if (provider == SqlProvider.SqlServer)
            sql += $" OPTION (MAXRECURSION {maxDepth})";

        return sql;
    }

    private enum SqlProvider { SqlServer, PostgreSql, Sqlite }

    private static SqlProvider Detect(string? providerName)
    {
        var name = providerName ?? string.Empty;
        if (name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.SqlServer;
        if (name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.PostgreSql;
        if (name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.Sqlite;

        throw new InvalidOperationException(
            $"Unsupported database provider '{name}' for hierarchy queries. " +
            "Supported: SQL Server, PostgreSQL, SQLite.");
    }

    private static string Quote(string identifier, SqlProvider provider)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.IndexOf('\0') >= 0)
            throw new ArgumentException("SQL identifier must be a non-empty string without NUL.", nameof(identifier));

        return provider == SqlProvider.SqlServer
            ? $"[{identifier.Replace("]", "]]")}]"
            : $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    // For "*" the recursive SELECT keeps "n.*"; for a specific column it becomes "n.{col}".
    private static string PrefixColumns(string selectColumns, string alias)
        => selectColumns == "*" ? $"{alias}.*" : $"{alias}.{selectColumns}";
}
