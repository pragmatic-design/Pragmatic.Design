using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Builds provider-aware LAG/LEAD window SQL for temporal-timeline queries.
/// </summary>
/// <remarks>
///     <para>
///         This helper delimits identifiers per provider and takes the real table/column names resolved
///         from the EF model. SQL Server bracket delimiters (<c>FROM [Xs]</c>) are a syntax error on
///         PostgreSQL and SQLite, and a pluralised type name points at a non-existent table when the
///         entity uses a custom <c>[Table]</c> name.
///     </para>
///     <para>
///         For a multi-parent temporal relation the window must PARTITION BY the parent key, otherwise
///         the previous/next validity of one parent's rows bleed across parents. Pass
///         <c>partitionColumn</c> for that case; pass null for a single global timeline.
///     </para>
///     <para>
///         Identifiers come from entity metadata (never end-user input) and are delimited per provider.
///     </para>
/// </remarks>
public static class TimelineCteSql
{
    /// <summary>
    ///     Builds the timeline CTE for the current provider, projecting exactly the four columns the
    ///     generated <c>{Entity}TimelineEntry</c> record binds (aliased to its property names).
    /// </summary>
    /// <param name="database">The <see cref="DatabaseFacade"/> (used only to read the provider name).</param>
    /// <param name="tableName">The table name (from entity metadata).</param>
    /// <param name="validFromColumn">The <c>ValidFrom</c> column name (from entity metadata).</param>
    /// <param name="validToColumn">The <c>ValidTo</c> column name (from entity metadata).</param>
    /// <param name="partitionColumn">
    ///     The parent-key column to PARTITION BY for a multi-parent relation, or null for a single global
    ///     timeline.
    /// </param>
    public static string Build(
        DatabaseFacade database,
        string tableName,
        string validFromColumn,
        string validToColumn,
        string? partitionColumn)
    {
        var provider = Detect(database.ProviderName);
        var table = Quote(tableName, provider);
        var validFrom = Quote(validFromColumn, provider);
        var validTo = Quote(validToColumn, provider);

        var over = partitionColumn is null
            ? $"OVER (ORDER BY {validFrom})"
            : $"OVER (PARTITION BY {Quote(partitionColumn, provider)} ORDER BY {validFrom})";

        return
            "WITH Timeline AS (" +
            $" SELECT {validFrom} AS ValidFrom, {validTo} AS ValidTo," +
            $" LAG({validTo}) {over} AS PreviousValidTo," +
            $" LEAD({validFrom}) {over} AS NextValidFrom" +
            $" FROM {table}" +
            ") SELECT ValidFrom, ValidTo, PreviousValidTo, NextValidFrom FROM Timeline ORDER BY ValidFrom";
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
            $"Unsupported database provider '{name}' for timeline queries. " +
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
}
