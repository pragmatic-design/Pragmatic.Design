using System.Diagnostics;
using Pragmatic.Maintenance;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     <see cref="MigrationRunner"/> execution helpers: transactional rollback safety, hook
///     dispatch, and the post-schema data-migration phase.
/// </summary>
public sealed partial class MigrationRunner
{
    private async Task RollbackSafeAsync(System.Data.Common.DbTransaction? transaction, string dbName, CancellationToken ct)
    {
        // Nothing to roll back if the transaction never began (BeginTransactionAsync threw).
        if (transaction is null) return;

        try
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MigrationDiagnostics.RollbackFailed(_logger, ex, dbName);
        }
    }

    private IMigrationHook[] ResolveHooks(string dbName)
    {
        if (hooks is null) return [];
        return hooks
            .Where(h => h.DatabaseName is null || string.Equals(h.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static async Task<bool> RunBeforeHooksAsync(
        IMigrationHook[] activeHooks, MigrationStepContext context, CancellationToken ct)
    {
        foreach (var hook in activeHooks)
        {
            if (!await hook.BeforeChangeAsync(context, ct).ConfigureAwait(false))
                return false;
        }
        return true;
    }

    private static async Task RunAfterHooksAsync(
        IMigrationHook[] activeHooks, MigrationStepContext context, CancellationToken ct)
    {
        foreach (var hook in activeHooks)
            await hook.AfterChangeAsync(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs registered <see cref="IDataMigration" />s that have not yet been applied, in a
    ///     dedicated transaction after the schema phase. Each runs once — tracked by name in
    ///     __PragmaticDataMigrations. Returns <paramref name="schemaResult" /> unchanged on
    ///     success, or a failed result if a data migration throws (its transaction is rolled back).
    /// </summary>
    private async Task<MigrationResult> ApplyDataMigrationsAsync(
        System.Data.Common.DbConnection connection,
        ISqlMigrationGenerator sqlGenerator,
        string dbName,
        Stopwatch sw,
        CancellationToken ct,
        MigrationResult schemaResult)
    {
        if (dataMigrations is null)
            return schemaResult;

        var applicable = dataMigrations
            .Where(d => d.DatabaseName is null || string.Equals(d.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Order)
            .ToArray();
        if (applicable.Length == 0)
            return schemaResult;

        var applied = 0;
        string? currentName = null;
        // BeginTransactionAsync is inside the try so that a begin failure is reported cleanly and
        // the catch/finally guard against a null transaction (no RollbackSafe/Dispose on null).
        System.Data.Common.DbTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            var tx = transaction; // non-null local for use inside the loop
            await DataMigrationStore.EnsureTableAsync(connection, tx, sqlGenerator, ct).ConfigureAwait(false);
            var alreadyApplied = await DataMigrationStore.GetAppliedNamesAsync(connection, tx, ct).ConfigureAwait(false);

            foreach (var dm in applicable)
            {
                if (alreadyApplied.Contains(dm.Name))
                    continue;

                currentName = dm.Name;
                var stepSw = Stopwatch.StartNew();
                await dm.MigrateAsync(connection, tx, ct).ConfigureAwait(false);
                stepSw.Stop();
                await DataMigrationStore.RecordAsync(connection, tx, dm.Name, stepSw.ElapsedMilliseconds, ct)
                    .ConfigureAwait(false);
                applied++;
                progressStream?.Report(new MigrationProgressEvent("migration",
                    $"[{dbName}] data migration applied: {dm.Name}", DatabaseName: dbName));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return schemaResult;
        }
        catch (Exception ex)
        {
            // transaction may be null if BeginTransactionAsync itself threw — guard the rollback.
            await RollbackSafeAsync(transaction, dbName, ct).ConfigureAwait(false);
            var msg = $"Data migration '{currentName}' failed: {ex.Message}";
            MigrationDiagnostics.MigrationFailed(_logger, ex, dbName, applied + 1, applicable.Length, currentName ?? "unknown");
            progressStream?.Report(new MigrationProgressEvent("error",
                $"[{dbName}] {msg}", IsError: true, DatabaseName: dbName));
            return new MigrationResult(false, schemaResult.ChangesApplied, sw.Elapsed,
                schemaResult.GeneratedSql, schemaResult.AppliedChanges, msg);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
