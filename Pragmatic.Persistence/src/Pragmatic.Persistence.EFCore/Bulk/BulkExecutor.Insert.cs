using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Persistence.EFCore.Bulk;

public static partial class BulkExecutor
{
    private static async Task<int> ExecuteInsertCoreAsync<T>(
        DbContext db,
        IReadOnlyList<T> entities,
        BulkEntityDescriptor<T> descriptor,
        BulkSqlTemplates templates,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        BulkInsertOptions options,
        CancellationToken ct) where T : class
    {
        if (templates.InsertColumns.Length == 0)
            return 0;

        // The raw-ADO bulk path bypasses SaveChanges, so TenantInterceptor never runs.
        // Stamp the ambient tenant onto ITenantEntity rows here (mirroring the interceptor)
        // so multi-tenant rows are not persisted with an empty TenantId.
        ApplyTenantId(db, entities);

        var entityName = typeof(T).Name;
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity($"BulkInsert.{entityName}");
        activity?.SetTag(DbTags.Operation, "INSERT");
        activity?.SetTag(DbTags.CollectionName, entityName);
        activity?.SetTag(DbTags.BulkBatchSize, options.BatchSize);
        activity?.SetTag(DbTags.BulkEntityCount, entities.Count);

        PersistenceDiagnostics.BulkOperations.Add(1,
            new KeyValuePair<string, object?>("operation", "insert"),
            new KeyValuePair<string, object?>("entity", entityName));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var totalAffected = 0;
            var connection = db.Database.GetDbConnection();

            // Open the shared EF connection only if we found it closed and
            // restore it in finally — otherwise an exception between Open and
            // the next caller would leave a pooled connection in Open state
            // (subtle leak under pressure).
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync(ct).ConfigureAwait(false);

            // Atomicity across batches: when the caller has no ambient transaction and the insert
            // spans more than one batch, open a local transaction so a mid-batch failure rolls back
            // all batches instead of leaving a partial commit. A caller-supplied transaction is
            // reused as-is (the caller owns commit/rollback).
            var ambientTransaction = db.Database.CurrentTransaction;
            var spansMultipleBatches = entities.Count > options.BatchSize;
            DbTransaction? localTransaction = null;
            if (ambientTransaction is null && spansMultipleBatches)
                localTransaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            try
            {
                var activeTransaction = ambientTransaction?.GetDbTransaction() ?? localTransaction;

                for (var offset = 0; offset < entities.Count; offset += options.BatchSize)
                {
                    var batchSize = Math.Min(options.BatchSize, entities.Count - offset);

                    var cmd = connection.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandTimeout = options.CommandTimeout;
                        cmd.Transaction = activeTransaction;

                        // Cached prefix + dynamic value rows
                        var sb = new StringBuilder(
                            templates.InsertSqlPrefix.Length + batchSize * templates.InsertColumns.Length * 10);
                        sb.Append(templates.InsertSqlPrefix);
                        AppendValueRows(sb, cmd, templates.InsertColumns, entities, descriptor, offset, batchSize,
                            auditTimestamp, auditUserId);

                        cmd.CommandText = sb.ToString();
                        totalAffected += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                if (localTransaction is not null)
                    await localTransaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (localTransaction is not null)
                    await localTransaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (localTransaction is not null)
                    await localTransaction.DisposeAsync().ConfigureAwait(false);

                if (openedHere && connection.State == ConnectionState.Open)
                    await connection.CloseAsync().ConfigureAwait(false);
            }

            stopwatch.Stop();
            PersistenceDiagnostics.BulkInsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("entity", entityName),
                new KeyValuePair<string, object?>("result", "success"));
            activity?.SetTag(DbTags.RowsAffected, totalAffected);
            activity?.SetSuccess();

            return totalAffected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PersistenceDiagnostics.BulkFailures.Add(1,
                new KeyValuePair<string, object?>("operation", "insert"),
                new KeyValuePair<string, object?>("entity", entityName));
            PersistenceDiagnostics.BulkInsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("entity", entityName),
                new KeyValuePair<string, object?>("result", "failure"));
            activity?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    ///     Appends parameterized value rows: "(@p0_0, @p0_1), (@p1_0, @p1_1), ..."
    ///     Shared by INSERT and UPSERT — the only per-batch dynamic part of the SQL.
    /// </summary>
    private static void AppendValueRows<T>(
        StringBuilder sb,
        DbCommand cmd,
        (string PropertyName, string ColumnName, BulkColumnRole Role)[] insertColumns,
        IReadOnlyList<T> entities,
        BulkEntityDescriptor<T> descriptor,
        int offset,
        int batchSize,
        DateTimeOffset? auditTimestamp,
        string? auditUserId) where T : class
    {
        for (var row = 0; row < batchSize; row++)
        {
            if (row > 0)
                sb.Append(", ");
            sb.Append('(');

            var entity = entities[offset + row];
            for (var col = 0; col < insertColumns.Length; col++)
            {
                if (col > 0)
                    sb.Append(", ");
                var paramName = $"@p{row}_{col}";
                sb.Append(paramName);

                var value = ResolveInsertValue(entity, insertColumns[col], descriptor, auditTimestamp, auditUserId);
                AddParameter(cmd, paramName, value);
            }

            sb.Append(')');
        }
    }

    /// <summary>
    ///     Stamps the current tenant onto <see cref="ITenantEntity"/> rows whose TenantId is empty,
    ///     mirroring <c>TenantInterceptor</c> for the raw-ADO bulk path that bypasses SaveChanges.
    ///     No-op when <typeparamref name="T"/> is not tenant-scoped, no DI is reachable, or no tenant
    ///     is resolved (single-tenant / system context) — matching the interceptor's behaviour.
    /// </summary>
    private static void ApplyTenantId<T>(DbContext db, IReadOnlyList<T> entities) where T : class
    {
        if (!typeof(ITenantEntity).IsAssignableFrom(typeof(T)))
            return;

        // Resolve the scoped ITenantContext from the DbContext's application service provider
        // (present when registered via AddDbContext; null for a manually constructed context).
        var services = db.GetInfrastructure().GetService<IServiceProvider>();
        var tenantContext = services?.GetService<ITenantContext>();
        if (tenantContext is not { IsResolved: true })
            return;

        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
            return;

        foreach (var entity in entities)
        {
            // entity is ITenantEntity by the type check above. Overwrite, not stamp-if-empty, so a
            // pre-set (wrong/other) TenantId cannot be bulk-inserted for another tenant —
            // mirrors the change-tracker interceptor.
            ((ITenantEntity)entity).TenantId = tenantId;
        }
    }

    private static object? ResolveInsertValue<T>(
        T entity,
        (string PropertyName, string ColumnName, BulkColumnRole Role) column,
        BulkEntityDescriptor<T> descriptor,
        DateTimeOffset? auditTimestamp,
        string? auditUserId) where T : class
    {
        return column.Role switch
        {
            // InsertOnly: CreatedAt -> auditTimestamp, CreatedBy -> auditUserId
            BulkColumnRole.InsertOnly when column.PropertyName.Equals("CreatedAt", StringComparison.Ordinal)
                => (object?)auditTimestamp ?? descriptor.ReadValue(entity, column.PropertyName),
            BulkColumnRole.InsertOnly when column.PropertyName.Equals("CreatedBy", StringComparison.Ordinal)
                => (object?)auditUserId ?? descriptor.ReadValue(entity, column.PropertyName),
            // SoftDelete defaults: IsDeleted=false, DeletedAt=null, DeletedBy=null
            BulkColumnRole.SoftDelete when column.PropertyName.Equals("IsDeleted", StringComparison.Ordinal)
                => false,
            BulkColumnRole.SoftDelete => null,
            // Key, LogicKey, Regular: read from entity
            _ => descriptor.ReadValue(entity, column.PropertyName),
        };
    }
}
