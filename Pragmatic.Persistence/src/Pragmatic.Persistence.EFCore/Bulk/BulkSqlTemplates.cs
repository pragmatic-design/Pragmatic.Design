namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Pre-built SQL template strings for bulk operations on a specific entity/provider combination.
///     Built once from <see cref="CachedEntityMetadata" /> and cached for reuse across batches.
///     Only the value-row placeholders are appended at runtime — the SQL structure is fully cached.
/// </summary>
internal sealed class BulkSqlTemplates
{
    /// <summary>Pre-filtered columns included in INSERT (Key, LogicKey, Regular, InsertOnly, SoftDelete).</summary>
    public required (string PropertyName, string ColumnName, BulkColumnRole Role)[] InsertColumns { get; init; }

    /// <summary>Pre-filtered columns included in UPDATE SET (Regular, UpdateOnly).</summary>
    public required (string PropertyName, string ColumnName, BulkColumnRole Role)[] UpdateColumns { get; init; }

    /// <summary>Primary key columns for upsert matching.</summary>
    public required (string PropertyName, string ColumnName, BulkColumnRole Role)[] MatchColumns_PK { get; init; }

    /// <summary>Logic key columns for upsert matching. Null if entity has no logic key.</summary>
    public (string PropertyName, string ColumnName, BulkColumnRole Role)[]? MatchColumns_LK { get; init; }

    /// <summary>INSERT SQL prefix: "INSERT INTO [Table] ([Col1], [Col2]) VALUES ".</summary>
    public required string InsertSqlPrefix { get; init; }

    /// <summary>UPSERT SQL prefix for PK match (provider-specific).</summary>
    public required string UpsertSqlPrefix_PK { get; init; }

    /// <summary>UPSERT SQL suffix for PK match (provider-specific).</summary>
    public required string UpsertSqlSuffix_PK { get; init; }

    /// <summary>UPSERT SQL prefix for LogicKey match. Null if entity has no logic key.</summary>
    public string? UpsertSqlPrefix_LK { get; init; }

    /// <summary>UPSERT SQL suffix for LogicKey match. Null if entity has no logic key.</summary>
    public string? UpsertSqlSuffix_LK { get; init; }

    /// <summary>Whether UPDATE SET includes audit columns requiring @audit_now/@audit_user parameters.</summary>
    public required bool HasAuditUpdateColumns { get; init; }

    /// <summary>Concurrency column (RowVersion). Null if entity is not concurrency-aware.</summary>
    public (string PropertyName, string ColumnName, BulkColumnRole Role)? ConcurrencyColumn { get; init; }

    /// <summary>
    ///     All columns for the concurrency-check upsert source (insert columns + RowVersion).
    ///     Null if no concurrency column exists.
    /// </summary>
    public (string PropertyName, string ColumnName, BulkColumnRole Role)[]? ConcurrencySourceColumns { get; init; }

    /// <summary>UPSERT SQL prefix for PK match with concurrency check. Null if not applicable.</summary>
    public string? UpsertConcurrencyPrefix_PK { get; init; }

    /// <summary>UPSERT SQL suffix for PK match with concurrency check. Null if not applicable.</summary>
    public string? UpsertConcurrencySuffix_PK { get; init; }

    /// <summary>UPSERT SQL prefix for LogicKey match with concurrency check. Null if not applicable.</summary>
    public string? UpsertConcurrencyPrefix_LK { get; init; }

    /// <summary>UPSERT SQL suffix for LogicKey match with concurrency check. Null if not applicable.</summary>
    public string? UpsertConcurrencySuffix_LK { get; init; }
}
