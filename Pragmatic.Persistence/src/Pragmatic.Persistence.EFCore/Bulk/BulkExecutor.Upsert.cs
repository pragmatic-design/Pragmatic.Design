using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Persistence.EFCore.Bulk;

public static partial class BulkExecutor
{
    private static async Task<int> ExecuteUpsertCoreAsync<T>(
        DbContext db,
        IReadOnlyList<T> entities,
        BulkEntityDescriptor<T> descriptor,
        BulkSqlTemplates templates,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        UpsertOptions options,
        CancellationToken ct) where T : class
    {
        if (templates.InsertColumns.Length == 0)
            return 0;

        var entityName = typeof(T).Name;
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity($"BulkUpsert.{entityName}");
        activity?.SetTag(DbTags.Operation, "UPSERT");
        activity?.SetTag(DbTags.CollectionName, entityName);
        activity?.SetTag(DbTags.BulkBatchSize, options.BatchSize);
        activity?.SetTag(DbTags.BulkEntityCount, entities.Count);
        activity?.SetTag(DbTags.BulkMatchOn, options.MatchOn.ToString());
        activity?.SetTag(DbTags.BulkConcurrencyCheck, options.ConcurrencyCheck);

        PersistenceDiagnostics.BulkOperations.Add(1,
            new KeyValuePair<string, object?>("operation", "upsert"),
            new KeyValuePair<string, object?>("entity", entityName));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Choose concurrency-check templates when requested and available
            var useConcurrency = options.ConcurrencyCheck && templates.ConcurrencySourceColumns is not null;
            string prefix, suffix;
            var sourceColumns = useConcurrency ? templates.ConcurrencySourceColumns! : templates.InsertColumns;

            if (useConcurrency)
            {
                (prefix, suffix) = options.MatchOn == UpsertMatch.LogicKey
                    ? (templates.UpsertConcurrencyPrefix_LK!, templates.UpsertConcurrencySuffix_LK!)
                    : (templates.UpsertConcurrencyPrefix_PK!, templates.UpsertConcurrencySuffix_PK!);
            }
            else
            {
                (prefix, suffix) = options.MatchOn == UpsertMatch.LogicKey
                    ? (templates.UpsertSqlPrefix_LK!, templates.UpsertSqlSuffix_LK!)
                    : (templates.UpsertSqlPrefix_PK, templates.UpsertSqlSuffix_PK);
            }

            var totalAffected = 0;
            var connection = db.Database.GetDbConnection();

            // Only close if WE opened it (see BulkExecutor.Insert.cs for rationale).
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync(ct).ConfigureAwait(false);

            try
            {
            for (var offset = 0; offset < entities.Count; offset += options.BatchSize)
            {
                var batchSize = Math.Min(options.BatchSize, entities.Count - offset);

                var cmd = connection.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandTimeout = options.CommandTimeout;
                    cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

                    // Add audit parameters once per batch if needed
                    if (templates.HasAuditUpdateColumns)
                    {
                        AddParameter(cmd, "@audit_now", auditTimestamp ?? (object)DBNull.Value);
                        AddParameter(cmd, "@audit_user", auditUserId ?? (object)DBNull.Value);
                    }

                    // Cached prefix + dynamic value rows + cached suffix
                    var sb = new StringBuilder(
                        prefix.Length + suffix.Length + batchSize * sourceColumns.Length * 10);
                    sb.Append(prefix);
                    AppendValueRows(sb, cmd, sourceColumns, entities, descriptor, offset, batchSize,
                        auditTimestamp, auditUserId);
                    sb.Append(suffix);

                    cmd.CommandText = sb.ToString();
                    totalAffected += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            }
            finally
            {
                if (openedHere && connection.State == ConnectionState.Open)
                    await connection.CloseAsync().ConfigureAwait(false);
            }

            stopwatch.Stop();
            PersistenceDiagnostics.BulkUpsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
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
                new KeyValuePair<string, object?>("operation", "upsert"),
                new KeyValuePair<string, object?>("entity", entityName));
            PersistenceDiagnostics.BulkUpsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("entity", entityName),
                new KeyValuePair<string, object?>("result", "failure"));
            activity?.RecordException(ex);
            throw;
        }
    }

    private static async Task<int> ExecuteUpsertSingleCoreAsync<T>(
        DbContext db,
        T entity,
        BulkEntityDescriptor<T> descriptor,
        BulkSqlTemplates templates,
        DateTimeOffset? auditTimestamp,
        string? auditUserId,
        UpsertMatch matchOn,
        CancellationToken ct) where T : class
    {
        if (templates.InsertColumns.Length == 0)
            return 0;

        var entityName = typeof(T).Name;
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity($"UpsertSingle.{entityName}");
        activity?.SetTag(DbTags.Operation, "UPSERT");
        activity?.SetTag(DbTags.CollectionName, entityName);
        activity?.SetTag(DbTags.BulkMatchOn, matchOn.ToString());

        PersistenceDiagnostics.BulkOperations.Add(1,
            new KeyValuePair<string, object?>("operation", "upsert_single"),
            new KeyValuePair<string, object?>("entity", entityName));

        var stopwatch = Stopwatch.StartNew();

        // Track whether WE opened the connection so we can restore its state
        // in finally — otherwise the single-entity upsert leaves a connection open.
        var connection = db.Database.GetDbConnection();
        var openedHere = false;
        try
        {
            var (prefix, suffix) = matchOn == UpsertMatch.LogicKey
                ? (templates.UpsertSqlPrefix_LK!, templates.UpsertSqlSuffix_LK!)
                : (templates.UpsertSqlPrefix_PK, templates.UpsertSqlSuffix_PK);

            openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync(ct).ConfigureAwait(false);

            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandTimeout = 30; // single-entity upsert: no options.CommandTimeout available on this code path
                cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

                // Add audit parameters if needed
                if (templates.HasAuditUpdateColumns)
                {
                    AddParameter(cmd, "@audit_now", auditTimestamp ?? (object)DBNull.Value);
                    AddParameter(cmd, "@audit_user", auditUserId ?? (object)DBNull.Value);
                }

                // Cached prefix + single value row + cached suffix
                var singleList = new[] { entity };
                var sb = new StringBuilder(
                    prefix.Length + suffix.Length + templates.InsertColumns.Length * 10);
                sb.Append(prefix);
                AppendValueRows(sb, cmd, templates.InsertColumns, singleList, descriptor, 0, 1,
                    auditTimestamp, auditUserId);
                sb.Append(suffix);

                cmd.CommandText = sb.ToString();
                var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                stopwatch.Stop();
                PersistenceDiagnostics.BulkUpsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("entity", entityName),
                    new KeyValuePair<string, object?>("result", "success"));
                activity?.SetTag(DbTags.RowsAffected, affected);
                activity?.SetSuccess();

                return affected;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PersistenceDiagnostics.BulkFailures.Add(1,
                new KeyValuePair<string, object?>("operation", "upsert_single"),
                new KeyValuePair<string, object?>("entity", entityName));
            PersistenceDiagnostics.BulkUpsertDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("entity", entityName),
                new KeyValuePair<string, object?>("result", "failure"));
            activity?.RecordException(ex);
            throw;
        }
        finally
        {
            // Only close if we opened it (don't disturb an ambient/transaction connection).
            if (openedHere && connection.State == ConnectionState.Open)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Validates that the requested match columns exist in the cached SQL templates.
    /// </summary>
    private static void ValidateMatchColumns(BulkSqlTemplates templates, UpsertMatch matchOn)
    {
        var matchColumns = matchOn == UpsertMatch.LogicKey
            ? templates.MatchColumns_LK
            : templates.MatchColumns_PK;

        if (matchColumns is null or { Length: 0 })
        {
            var roleName = matchOn == UpsertMatch.LogicKey ? "LogicKey" : "Key";
            throw new InvalidOperationException(
                $"No columns with role {roleName} found. " +
                $"Cannot perform upsert with MatchOn = {matchOn}.");
        }
    }
}
