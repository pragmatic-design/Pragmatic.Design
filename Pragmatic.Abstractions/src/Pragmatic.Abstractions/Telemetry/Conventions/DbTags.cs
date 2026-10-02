namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for database operations, aligned with OTel semantic conventions.
/// </summary>
/// <remarks>
///     Standard OTel tags (db.*) follow https://opentelemetry.io/docs/specs/semconv/database/.
///     Pragmatic-specific tags use the pragmatic.db.* prefix.
/// </remarks>
public static class DbTags
{
    /// <summary>The database operation name (e.g., "SELECT", "INSERT", "UPSERT").</summary>
    public const string Operation = "db.operation.name";

    /// <summary>The database collection (table) name.</summary>
    public const string CollectionName = "db.collection.name";

    /// <summary>The number of rows affected by the operation.</summary>
    public const string RowsAffected = "db.response.rows_affected";

    /// <summary>Batch size for bulk operations.</summary>
    public const string BulkBatchSize = "pragmatic.db.bulk.batch_size";

    /// <summary>The database name. OTel semconv, hence no prefix.</summary>
    public const string Namespace = "db.namespace";

    /// <summary>The EF Core provider in use.</summary>
    public const string Provider = "pragmatic.db.provider";

    /// <summary>Number of entities in the bulk operation.</summary>
    public const string BulkEntityCount = "pragmatic.db.bulk.entity_count";

    /// <summary>The properties an upsert matches on.</summary>
    public const string BulkMatchOn = "pragmatic.db.bulk.match_on";

    /// <summary>Whether the upsert performed a concurrency check.</summary>
    public const string BulkConcurrencyCheck = "pragmatic.db.bulk.concurrency_check";

    /// <summary>Number of query filters applied.</summary>
    public const string FilterCount = "pragmatic.db.filter_count";

    /// <summary>The active filter mode (e.g. "normal", "admin", "raw").</summary>
    public const string FilterMode = "pragmatic.db.filter_mode";

    /// <summary>The names of the filters applied.</summary>
    public const string FilterNames = "pragmatic.db.filter_names";
}
