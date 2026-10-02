namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Cached table and column metadata resolved from the EF Core model.
///     Immutable after construction; safe to share across threads.
/// </summary>
internal sealed class CachedEntityMetadata
{
    /// <summary>The database table name.</summary>
    public required string TableName { get; init; }

    /// <summary>The database schema (null for SQLite or default schema).</summary>
    public required string? Schema { get; init; }

    /// <summary>
    ///     Column mappings: PropertyName to (ColumnName, Role).
    ///     Preserves the order from the descriptor.
    /// </summary>
    public required (string PropertyName, string ColumnName, BulkColumnRole Role)[] Columns { get; init; }

    /// <summary>The detected database provider type.</summary>
    public required ProviderType Provider { get; init; }
}
