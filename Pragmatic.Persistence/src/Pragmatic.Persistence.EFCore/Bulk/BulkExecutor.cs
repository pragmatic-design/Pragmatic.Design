using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Executes bulk INSERT and UPSERT operations using raw ADO.NET with provider-specific SQL.
///     SQL template strings (prefix/suffix) are built once per entity/provider and cached —
///     only value-row placeholders are appended at runtime.
///     Called from source-generated repository methods.
/// </summary>
public static partial class BulkExecutor
{
    // Cache key: (DbContextType, EntityType, ProviderName) → metadata + templates as ONE entry.
    // ProviderName is included because the same DbContext type can be configured for different providers
    // (e.g., in tests), and metadata (table names, column names, provider-specific SQL) differs.
    // Metadata and the SQL templates derived from it are stored together so the paired lookup is atomic:
    // a reader can never observe templates built from a different metadata snapshot than is cached.
    private static readonly ConcurrentDictionary<(Type DbContextType, Type EntityType, string? ProviderName), CachedBulkEntry> EntryCache = new();

    // Cache key: (DbContextType, ProviderName) → ProviderType
    private static readonly ConcurrentDictionary<(Type, string?), ProviderType> ProviderCache = new();

    // Combined cache entry: metadata and the SQL templates built from that exact metadata snapshot.
    private sealed record CachedBulkEntry(CachedEntityMetadata Metadata, BulkSqlTemplates Templates);

    /// <summary>
    ///     Inserts entities in batches using multi-row INSERT VALUES statements via ADO.NET.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="db">The EF Core DbContext providing the connection and model metadata.</param>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="descriptor">Source-generated descriptor with column roles and value reader.</param>
    /// <param name="auditTimestamp">Timestamp for InsertOnly audit columns (CreatedAt). Null to skip audit.</param>
    /// <param name="auditUserId">User ID for InsertOnly audit columns (CreatedBy). Null to skip.</param>
    /// <param name="options">Batch size and timeout options. Null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of rows inserted.</returns>
    public static Task<int> InsertAsync<T>(
        DbContext db,
        IReadOnlyList<T> entities,
        BulkEntityDescriptor<T> descriptor,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        BulkInsertOptions? options,
        CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (entities.Count == 0)
            return Task.FromResult(0);

        var opts = options ?? new BulkInsertOptions();
        var templates = GetCachedTemplates(db, descriptor);

        return ExecuteInsertCoreAsync(db, entities, descriptor, templates, auditTimestamp, auditUserId, opts, ct);
    }

    /// <summary>
    ///     Upserts (insert-or-update) entities in batches using provider-specific SQL.
    ///     Uses MERGE for SQL Server, INSERT ON CONFLICT for PostgreSQL and SQLite.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="db">The EF Core DbContext providing the connection and model metadata.</param>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="descriptor">Source-generated descriptor with column roles and value reader.</param>
    /// <param name="auditTimestamp">Timestamp for audit columns. Null to skip audit.</param>
    /// <param name="auditUserId">User ID for audit columns. Null to skip.</param>
    /// <param name="options">Match strategy, batch size, and timeout options. Null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of rows affected.</returns>
    public static Task<int> UpsertAsync<T>(
        DbContext db,
        IReadOnlyList<T> entities,
        BulkEntityDescriptor<T> descriptor,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        UpsertOptions? options,
        CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (entities.Count == 0)
            return Task.FromResult(0);

        var opts = options ?? new UpsertOptions();
        var templates = GetCachedTemplates(db, descriptor);

        ValidateMatchColumns(templates, opts.MatchOn);

        return ExecuteUpsertCoreAsync(db, entities, descriptor, templates, auditTimestamp, auditUserId, opts, ct);
    }

    /// <summary>
    ///     Upserts a single entity using provider-specific SQL.
    ///     Caches the SQL template since single-entity SQL shape is always the same.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="db">The EF Core DbContext providing the connection and model metadata.</param>
    /// <param name="entity">The entity to upsert.</param>
    /// <param name="descriptor">Source-generated descriptor with column roles and value reader.</param>
    /// <param name="auditTimestamp">Timestamp for audit columns. Null to skip audit.</param>
    /// <param name="auditUserId">User ID for audit columns. Null to skip.</param>
    /// <param name="matchOn">Strategy for matching existing entities.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rows affected (1 for insert or update, 0 if no change).</returns>
    public static Task<int> UpsertSingleAsync<T>(
        DbContext db,
        T entity,
        BulkEntityDescriptor<T> descriptor,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        UpsertMatch matchOn,
        CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(descriptor);

        var templates = GetCachedTemplates(db, descriptor);

        ValidateMatchColumns(templates, matchOn);

        return ExecuteUpsertSingleCoreAsync(db, entity, descriptor, templates, auditTimestamp, auditUserId, matchOn, ct);
    }

    /// <summary>
    ///     Resolves and caches entity metadata together with the SQL templates derived from it.
    ///     Both are computed from the same snapshot inside a single <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>
    ///     factory, so concurrent callers never see templates built from a different metadata instance.
    /// </summary>
    private static BulkSqlTemplates GetCachedTemplates<T>(
        DbContext db,
        BulkEntityDescriptor<T> descriptor) where T : class
    {
        var providerName = db.Database.ProviderName;
        var key = (db.GetType(), typeof(T), providerName);

        var entry = EntryCache.GetOrAdd(key, _ =>
        {
            var metadata = BuildMetadata(db, descriptor);
            return new CachedBulkEntry(metadata, BuildSqlTemplates(metadata));
        });

        return entry.Templates;
    }
}
