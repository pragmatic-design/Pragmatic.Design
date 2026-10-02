using System.Text;

namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Builds and caches SQL template strings (prefix/suffix) from entity metadata.
///     Called once per entity/provider combination — subsequent batches reuse the cached templates.
/// </summary>
public static partial class BulkExecutor
{
    internal static BulkSqlTemplates BuildSqlTemplates(CachedEntityMetadata metadata)
    {
        var insertColumns = metadata.Columns
            .Where(c => c.Role is not (BulkColumnRole.Computed or BulkColumnRole.UpdateOnly))
            .ToArray();

        var updateColumns = metadata.Columns
            .Where(c => c.Role is BulkColumnRole.Regular or BulkColumnRole.UpdateOnly)
            .ToArray();

        var matchColumnsPK = metadata.Columns
            .Where(c => c.Role == BulkColumnRole.Key)
            .ToArray();

        var matchColumnsLK = metadata.Columns
            .Where(c => c.Role == BulkColumnRole.LogicKey)
            .ToArray();

        var hasLogicKey = matchColumnsLK.Length > 0;
        var hasAuditUpdateColumns = updateColumns.Any(c => c.Role == BulkColumnRole.UpdateOnly);
        var provider = metadata.Provider;

        // Build INSERT prefix (shared by PG/SQLite upsert as well)
        var insertPrefix = BuildInsertPrefix(FormatTableName(metadata), insertColumns, provider);

        // Build UPSERT templates for PK match
        var (upsertPrefixPK, upsertSuffixPK) = BuildUpsertTemplates(
            FormatTableName(metadata), insertColumns, updateColumns, matchColumnsPK, provider, insertPrefix);

        // Build UPSERT templates for LogicKey match (if applicable)
        string? upsertPrefixLK = null;
        string? upsertSuffixLK = null;
        if (hasLogicKey)
        {
            (upsertPrefixLK, upsertSuffixLK) = BuildUpsertTemplates(
                FormatTableName(metadata), insertColumns, updateColumns, matchColumnsLK, provider, insertPrefix);
        }

        // Concurrency check: find RowVersion column for optimistic concurrency in upsert
        var concurrencyColumn = metadata.Columns
            .FirstOrDefault(c => c.Role == BulkColumnRole.Computed);
        var hasConcurrency = concurrencyColumn != default;

        (string PropertyName, string ColumnName, BulkColumnRole Role)[]? concurrencySourceColumns = null;
        string? upsertConcurrencyPrefixPK = null, upsertConcurrencySuffixPK = null;
        string? upsertConcurrencyPrefixLK = null, upsertConcurrencySuffixLK = null;

        if (hasConcurrency)
        {
            // Source columns = insert columns + RowVersion (for referencing in WHEN MATCHED AND)
            concurrencySourceColumns = insertColumns.Append(concurrencyColumn).ToArray();
            var concurrencyInsertPrefix = BuildInsertPrefix(FormatTableName(metadata), concurrencySourceColumns, provider);

            (upsertConcurrencyPrefixPK, upsertConcurrencySuffixPK) = BuildUpsertTemplates(
                FormatTableName(metadata), concurrencySourceColumns, updateColumns, matchColumnsPK,
                provider, concurrencyInsertPrefix, concurrencyColumn);

            if (hasLogicKey)
            {
                (upsertConcurrencyPrefixLK, upsertConcurrencySuffixLK) = BuildUpsertTemplates(
                    FormatTableName(metadata), concurrencySourceColumns, updateColumns, matchColumnsLK,
                    provider, concurrencyInsertPrefix, concurrencyColumn);
            }
        }

        return new BulkSqlTemplates
        {
            InsertColumns = insertColumns,
            UpdateColumns = updateColumns,
            MatchColumns_PK = matchColumnsPK,
            MatchColumns_LK = hasLogicKey ? matchColumnsLK : null,
            InsertSqlPrefix = insertPrefix,
            UpsertSqlPrefix_PK = upsertPrefixPK,
            UpsertSqlSuffix_PK = upsertSuffixPK,
            UpsertSqlPrefix_LK = upsertPrefixLK,
            UpsertSqlSuffix_LK = upsertSuffixLK,
            HasAuditUpdateColumns = hasAuditUpdateColumns,
            ConcurrencyColumn = hasConcurrency ? concurrencyColumn : null,
            ConcurrencySourceColumns = concurrencySourceColumns,
            UpsertConcurrencyPrefix_PK = upsertConcurrencyPrefixPK,
            UpsertConcurrencySuffix_PK = upsertConcurrencySuffixPK,
            UpsertConcurrencyPrefix_LK = upsertConcurrencyPrefixLK,
            UpsertConcurrencySuffix_LK = upsertConcurrencySuffixLK,
        };
    }

    private static string BuildInsertPrefix(
        string tableName,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        ProviderType provider)
    {
        var sb = new StringBuilder(128);
        sb.Append("INSERT INTO ");
        sb.Append(tableName);
        sb.Append(" (");

        for (var i = 0; i < insertColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(QuoteIdentifier(insertColumns[i].ColumnName, provider));
        }

        sb.Append(") VALUES ");
        return sb.ToString();
    }

    private static (string Prefix, string Suffix) BuildUpsertTemplates(
        string tableName,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] updateColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] matchColumns,
        ProviderType provider,
        string insertPrefix,
        (string PropertyName, string ColumnName, BulkColumnRole Role)? concurrencyColumn = null)
    {
        return provider switch
        {
            ProviderType.SqlServer => BuildMergeTemplates(tableName, insertColumns, updateColumns, matchColumns, provider, concurrencyColumn),
            ProviderType.PostgreSql => BuildPgUpsertTemplates(insertColumns, updateColumns, matchColumns, provider, insertPrefix, concurrencyColumn),
            ProviderType.Sqlite => BuildSqliteUpsertTemplates(insertColumns, updateColumns, matchColumns, provider, insertPrefix, concurrencyColumn),
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };
    }

    // ──────────────────────────────────────────────────────────────
    // SQL Server: MERGE INTO ... USING (VALUES ...) ...
    // ──────────────────────────────────────────────────────────────
    private static (string Prefix, string Suffix) BuildMergeTemplates(
        string tableName,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] updateColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] matchColumns,
        ProviderType provider,
        (string PropertyName, string ColumnName, BulkColumnRole Role)? concurrencyColumn = null)
    {
        var prefix = $"MERGE INTO {tableName} AS t USING (VALUES ";

        var sb = new StringBuilder(256);

        // Close VALUES, open source alias
        sb.Append(") AS s(");
        for (var i = 0; i < insertColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(QuoteIdentifier(insertColumns[i].ColumnName, provider));
        }

        // ON (match condition)
        sb.Append(") ON (");
        for (var i = 0; i < matchColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(" AND ");
            var mc = QuoteIdentifier(matchColumns[i].ColumnName, provider);
            sb.Append($"t.{mc} = s.{mc}");
        }
        sb.Append(')');

        // WHEN MATCHED [AND t.RowVersion = s.RowVersion] THEN UPDATE SET
        if (updateColumns.Length > 0)
        {
            sb.Append(" WHEN MATCHED");
            if (concurrencyColumn is { } cc)
            {
                var qRv = QuoteIdentifier(cc.ColumnName, provider);
                sb.Append($" AND t.{qRv} = s.{qRv}");
            }
            sb.Append(" THEN UPDATE SET ");
            AppendUpdateSetClause(sb, updateColumns, provider, "s");
        }

        // WHEN NOT MATCHED THEN INSERT (exclude concurrency column from insert target)
        var insertTargetColumns = concurrencyColumn is not null
            ? insertColumns.Where(c => c.Role != BulkColumnRole.Computed).ToArray()
            : insertColumns;

        sb.Append(" WHEN NOT MATCHED THEN INSERT (");
        for (var i = 0; i < insertTargetColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(QuoteIdentifier(insertTargetColumns[i].ColumnName, provider));
        }

        sb.Append(") VALUES (");
        for (var i = 0; i < insertTargetColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append($"s.{QuoteIdentifier(insertTargetColumns[i].ColumnName, provider)}");
        }
        sb.Append(");");

        return (prefix, sb.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // PostgreSQL: INSERT ... ON CONFLICT (...) DO UPDATE SET ...
    // ──────────────────────────────────────────────────────────────
    private static (string Prefix, string Suffix) BuildPgUpsertTemplates(
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] updateColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] matchColumns,
        ProviderType provider,
        string insertPrefix,
        (string PropertyName, string ColumnName, BulkColumnRole Role)? concurrencyColumn = null)
    {
        var sb = new StringBuilder(256);

        sb.Append(" ON CONFLICT (");
        for (var i = 0; i < matchColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(QuoteIdentifier(matchColumns[i].ColumnName, provider));
        }
        sb.Append(')');

        if (updateColumns.Length > 0)
        {
            sb.Append(" DO UPDATE SET ");
            AppendUpdateSetClause(sb, updateColumns, provider, "EXCLUDED");
            // Concurrency check: skip update if RowVersion doesn't match
            if (concurrencyColumn is { } cc)
            {
                var qRv = QuoteIdentifier(cc.ColumnName, provider);
                sb.Append($" WHERE {qRv} = EXCLUDED.{qRv}");
            }
        }
        else
        {
            sb.Append(" DO NOTHING");
        }

        return (insertPrefix, sb.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // SQLite: INSERT ... ON CONFLICT(...) DO UPDATE SET ...
    // ──────────────────────────────────────────────────────────────
    private static (string Prefix, string Suffix) BuildSqliteUpsertTemplates(
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] updateColumns,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] matchColumns,
        ProviderType provider,
        string insertPrefix,
        (string PropertyName, string ColumnName, BulkColumnRole Role)? concurrencyColumn = null)
    {
        var sb = new StringBuilder(256);

        // SQLite: no space before opening paren
        sb.Append(" ON CONFLICT(");
        for (var i = 0; i < matchColumns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(QuoteIdentifier(matchColumns[i].ColumnName, provider));
        }
        sb.Append(')');

        if (updateColumns.Length > 0)
        {
            sb.Append(" DO UPDATE SET ");
            // SQLite uses lowercase 'excluded'
            AppendUpdateSetClause(sb, updateColumns, provider, "excluded");
            // Concurrency check: skip update if RowVersion doesn't match
            if (concurrencyColumn is { } cc)
            {
                var qRv = QuoteIdentifier(cc.ColumnName, provider);
                sb.Append($" WHERE {qRv} = excluded.{qRv}");
            }
        }
        else
        {
            sb.Append(" DO NOTHING");
        }

        return (insertPrefix, sb.ToString());
    }

    /// <summary>
    ///     Appends the UPDATE SET clause: "col1 = source.col1, col2 = @audit_now, ..."
    ///     For MERGE, sourceAlias is "s"; for PG, "EXCLUDED"; for SQLite, "excluded".
    /// </summary>
    private static void AppendUpdateSetClause(
        StringBuilder sb,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] updateColumns,
        ProviderType provider,
        string sourceAlias)
    {
        var isMerge = provider == ProviderType.SqlServer;
        var first = true;

        foreach (var col in updateColumns)
        {
            if (!first)
                sb.Append(", ");
            first = false;

            var qCol = QuoteIdentifier(col.ColumnName, provider);
            var target = isMerge ? $"t.{qCol}" : qCol;

            if (col.Role == BulkColumnRole.UpdateOnly && col.PropertyName.Equals("UpdatedAt", StringComparison.Ordinal))
                sb.Append($"{target} = @audit_now");
            else if (col.Role == BulkColumnRole.UpdateOnly && col.PropertyName.Equals("UpdatedBy", StringComparison.Ordinal))
                sb.Append($"{target} = @audit_user");
            else
                sb.Append(isMerge ? $"{target} = {sourceAlias}.{qCol}" : $"{target} = {sourceAlias}.{qCol}");
        }
    }
}
