using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Maintenance;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Default migration runner: introspect current schema → compute diff → generate SQL → execute.
///     Executes each change in its own command inside a transaction for atomicity and precise error reporting.
/// </summary>
public sealed partial class MigrationRunner(
    MigrationProviderFactory providerFactory,
    ISchemaDiffEngine diffEngine,
    ILogger<MigrationRunner>? logger = null,
    IMigrationProgressStream? progressStream = null,
    IEnumerable<IMigrationSeedProvider>? seedProviders = null,
    IEnumerable<IMigrationHook>? hooks = null,
    IEnumerable<IDataMigration>? dataMigrations = null,
    IMigrationLeaderElection? leaderElection = null) : IMigrationRunner
{
    private readonly ILogger _logger = logger ?? NullLogger<MigrationRunner>.Instance;
    private readonly IMigrationLeaderElection? _explicitLeaderElection = leaderElection;

    public async Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var providerName = context.DesiredSchema.ProviderName;
        var dbName = context.DesiredSchema.DatabaseName ?? "default";

        // Check database filter
        if (!context.Options.ShouldMigrate(dbName))
        {
            MigrationDiagnostics.MigrationSkipped(_logger, dbName);
            progressStream?.Report(new MigrationProgressEvent("complete",
                $"[{dbName}] Skipped (not in database filter)", DatabaseName: dbName));
            return MigrationResult.NoChanges;
        }

        using var activity = MigrationDiagnostics.ActivitySource.StartActivity("Migrations.Migrate");
        activity?.SetTag(DbTags.Namespace, dbName);
        activity?.SetTag(DbTags.Provider, providerName);

        MigrationDiagnostics.MigrationStarted(_logger, dbName, providerName, context.DesiredSchema.Hash);

        // Leader election: only the leader executes migrations in a distributed setup
        var election = ResolveLeaderElection(context, providerName);
        var isLeader = await election.TryBecomeLeaderAsync(ct).ConfigureAwait(false);
        if (!isLeader)
        {
            progressStream?.Report(new MigrationProgressEvent("waiting",
                $"[{dbName}] Waiting for leader to complete migrations...", DatabaseName: dbName));
            await election.WaitForLeaderCompletionAsync(ct).ConfigureAwait(false);

            // The lock clearing only says the leader LEFT, not that it succeeded — it is released
            // on failure too. Returning success here would let every follower boot on a schema the
            // leader never managed to migrate, so verify before claiming the database is ready.
            return await VerifyFollowerSchemaAsync(context, dbName, providerName, ct).ConfigureAwait(false);
        }

        // Guarantee ReleaseLeadershipAsync runs on every exit path
        // (dry-run, breaking-blocked, exception, success) so followers don't
        // wait forever for a leader that already left the room.
        try
        {
            var result = await RunAsLeaderAsync(context, sw, dbName, providerName, activity, ct).ConfigureAwait(false);
            MigrationDiagnostics.RecordRun(
                dbName, providerName, result.Success, result.ChangesApplied, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception)
        {
            // An exception escaping the runner is still a migration that ran and failed — the
            // metrics must not show only the failures that came back as a MigrationResult.
            MigrationDiagnostics.RecordRun(dbName, providerName, success: false, changesApplied: 0,
                sw.Elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            await election.ReleaseLeadershipAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<MigrationResult> RunAsLeaderAsync(
        MigrationContext context,
        Stopwatch sw,
        string dbName,
        string? providerName,
        Activity? activity,
        CancellationToken ct)
    {
        var introspector = providerFactory.GetIntrospector(providerName);
        var sqlGenerator = providerFactory.GetGenerator(providerName);
        var connectionFactory = providerFactory.GetConnectionFactory(providerName);

        // Phase 0: Create and open connection (with retry on transient failures) + acquire advisory lock
        var connection = await MigrationResilience.OpenWithRetryAsync(
            () => connectionFactory.CreateOpenConnectionAsync(context.ConnectionString, ct), ct).ConfigureAwait(false);
        await using var _ = connection.ConfigureAwait(false);
        var migrationLock = await MigrationLock.AcquireAsync(
            connection, providerName ?? "", ct, context.Options.LockTimeout).ConfigureAwait(false);
        await using var __ = migrationLock.ConfigureAwait(false);

        // Phase 1: Introspect current schema
        progressStream?.Report(new MigrationProgressEvent("analyzing",
            $"[{dbName}] Introspecting current schema ({introspector.ProviderName})...",
            DatabaseName: dbName));

        var current = await introspector.IntrospectAsync(connection, ct).ConfigureAwait(false);

        // Fast path: both hashes come from SchemaHasher, which normalises exactly what the diff
        // compares — so equal hashes mean the diff would find nothing. It is skipped when the host
        // opted out of dropping unknown tables, because then the desired schema is deliberately a
        // subset of the database and the two identities are not meant to match.
        if (!context.Options.ManageDeclaredTablesOnly && current.Hash == context.DesiredSchema.Hash)
        {
            MigrationDiagnostics.SchemaUpToDate(_logger, dbName, current.Hash);
            progressStream?.Report(new MigrationProgressEvent("complete",
                $"[{dbName}] Schema up to date (hash {current.Hash})", ProgressPercent: 100.0, DatabaseName: dbName));
            return await ApplyDataMigrationsAsync(connection, sqlGenerator, dbName, sw, ct, MigrationResult.NoChanges)
                .ConfigureAwait(false);
        }

        // Phase 2: Compute diff
        progressStream?.Report(new MigrationProgressEvent("analyzing",
            $"[{dbName}] Computing schema diff...", DatabaseName: dbName));
        var diff = diffEngine.ComputeDiff(context.DesiredSchema, current, !context.Options.ManageDeclaredTablesOnly);

        if (!diff.HasChanges)
        {
            MigrationDiagnostics.SchemaUpToDate(_logger, dbName, context.DesiredSchema.Hash);
            progressStream?.Report(new MigrationProgressEvent("complete",
                $"[{dbName}] No changes needed", ProgressPercent: 100.0, DatabaseName: dbName));
            return await ApplyDataMigrationsAsync(connection, sqlGenerator, dbName, sw, ct, MigrationResult.NoChanges)
                .ConfigureAwait(false);
        }

        var total = diff.Changes.Length;
        var breakingCount = diff.Changes.Count(c => c.IsBreaking);
        MigrationDiagnostics.DiffComputed(_logger, dbName, total, breakingCount);
        activity?.SetTag(MigrationTags.ChangeCount, total);
        activity?.SetTag(MigrationTags.BreakingCount, breakingCount);

        // Phase 3: Generate full SQL (for audit/dry-run) + per-change scripts
        var fullSql = sqlGenerator.GenerateScript(diff);

        // Dry run: return detailed change list without executing
        if (context.Options.DryRun)
        {
            MigrationDiagnostics.DryRunComplete(_logger, dbName, total);
            var dryRunSummary = BuildDryRunSummary(diff, dbName);
            progressStream?.Report(new MigrationProgressEvent("complete",
                dryRunSummary, ProgressPercent: 100.0, DatabaseName: dbName));
            return new MigrationResult(true, 0, sw.Elapsed, fullSql, diff.Changes, null);
        }

        // Safety check: breaking changes require Force
        if (diff.HasBreakingChanges && !context.Options.Force)
        {
            MigrationDiagnostics.BreakingChangesBlocked(_logger, dbName, breakingCount);
            activity?.SetStatus(ActivityStatusCode.Error, "Breaking changes blocked");
            var suggestions = ImmutableArray.Create(
                $"{breakingCount} breaking change(s) detected — these may cause data loss",
                "Run with DryRun=true to inspect the SQL before applying",
                "Use Force=true to apply breaking changes");
            progressStream?.Report(new MigrationProgressEvent("error",
                $"[{dbName}] Blocked: {breakingCount} breaking changes. Use Force=true.",
                IsError: true, DatabaseName: dbName));
            return new MigrationResult(false, 0, sw.Elapsed, fullSql, diff.Changes,
                $"Breaking changes detected ({breakingCount}). Use Force=true to apply.") { Suggestions = suggestions };
        }

        // Phase 4: Execute each change in a transaction
        progressStream?.Report(new MigrationProgressEvent("applying",
            $"[{dbName}] Applying {total} changes...", DatabaseName: dbName));

        var appliedCount = 0;
        // 0-based index of the change currently being processed. Tracked separately from
        // appliedCount (which also counts deferred/skipped steps) so the catch reports the
        // EXACT change that failed rather than inferring an index from a count.
        var currentIndex = -1;
        var deferredConcurrent = new List<AddIndex>();

        var (rebuildTables, createdTables) = PlanTableRebuilds(diff, sqlGenerator);
        var rebuiltTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // BeginTransactionAsync is inside the try so a failure to even open the transaction is
        // reported cleanly. transaction stays null in that case and the catch/finally must guard.
        System.Data.Common.DbTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            var tx = transaction; // non-null local for use inside the loop
            var activeHooks = ResolveHooks(dbName);

            for (var i = 0; i < total; i++)
            {
                currentIndex = i;
                var change = diff.Changes[i];
                var step = i + 1;

                MigrationDiagnostics.ApplyingChange(_logger, dbName, step, total, change.Description);

                var changeTable = SchemaChangeTarget.TableNameOf(change);

                // A rebuild-only change on a table created by this very migration is already
                // satisfied: the provider's CREATE TABLE emitted the target shape inline (SQLite
                // puts FKs and the PK in the CREATE). Rebuilding would mean renaming a table that
                // did not exist a moment ago.
                if (changeTable is not null &&
                    createdTables.Contains(changeTable) &&
                    sqlGenerator.GetRebuildTableName(change) is not null)
                {
                    appliedCount = step;
                    progressStream?.Report(new MigrationProgressEvent("applying",
                        $"[{dbName}] [skip] {step}/{total}: {change.Description} (already part of CREATE TABLE {changeTable})",
                        ProgressPercent: (double)step / total * 100, DatabaseName: dbName));
                    continue;
                }

                // Table rebuild (SQLite): one rebuild covers every change on that table.
                if (changeTable is { } targetTable && rebuildTables.Contains(targetTable))
                {
                    appliedCount = step;
                    if (!rebuiltTables.Add(targetTable))
                    {
                        progressStream?.Report(new MigrationProgressEvent("applying",
                            $"[{dbName}] [rebuild] {step}/{total}: {change.Description} (covered by the {targetTable} rebuild)",
                            ProgressPercent: (double)step / total * 100, DatabaseName: dbName));
                        continue;
                    }

                    var rebuildSql = BuildTableRebuildSql(sqlGenerator, context.DesiredSchema, current, targetTable);
                    var rebuildCmd = connection.CreateCommand();
                    await using (rebuildCmd.ConfigureAwait(false))
                    {
                        rebuildCmd.Transaction = tx;
                        rebuildCmd.CommandText = rebuildSql;
                        rebuildCmd.CommandTimeout = (int)context.Options.Timeout.TotalSeconds;
                        await rebuildCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    MigrationDiagnostics.ChangeApplied(_logger, dbName, step, total, change.Description);
                    progressStream?.Report(new MigrationProgressEvent("applying",
                        $"[{dbName}] [OK] {step}/{total}: rebuilt table {targetTable}",
                        ProgressPercent: (double)step / total * 100, DatabaseName: dbName));
                    continue;
                }

                var changeSql = sqlGenerator.GenerateChangeScript(change);
                var stepContext = new MigrationStepContext(connection, tx, change);

                // Concurrent index build: defer to post-commit phase (out of transaction).
                // Hooks are intentionally not fired here — they target the migration transaction
                // that this change is leaving.
                if (context.Options.ConcurrentIndexes && change is AddIndex addIdx)
                {
                    deferredConcurrent.Add(addIdx with { IsConcurrent = true });
                    appliedCount = step;
                    progressStream?.Report(new MigrationProgressEvent("applying",
                        $"[{dbName}] [defer] {step}/{total}: {change.Description} (post-commit concurrent build)",
                        ProgressPercent: (double)step / total * 100, DatabaseName: dbName));
                    continue;
                }

                // Before hook — can skip the change by returning false
                if (!await RunBeforeHooksAsync(activeHooks, stepContext, ct).ConfigureAwait(false))
                {
                    appliedCount = step;
                    progressStream?.Report(new MigrationProgressEvent("applying",
                        $"[{dbName}] [SKIP] {step}/{total}: {change.Description} (skipped by hook)",
                        ProgressPercent: (double)step / total * 100, DatabaseName: dbName));
                    continue;
                }

                var cmd = connection.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = changeSql;
                    cmd.CommandTimeout = (int)context.Options.Timeout.TotalSeconds;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // After hook — run custom logic (data migration, population, etc.)
                await RunAfterHooksAsync(activeHooks, stepContext, ct).ConfigureAwait(false);

                appliedCount = step;
                MigrationDiagnostics.ChangeApplied(_logger, dbName, step, total, change.Description);

                var pct = (double)step / total * 100;
                var indicator = change.IsBreaking ? "!!" : "OK";
                progressStream?.Report(new MigrationProgressEvent("applying",
                    $"[{dbName}] [{indicator}] {step}/{total}: {change.Description}",
                    ProgressPercent: pct, DatabaseName: dbName));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // transaction may be null if BeginTransactionAsync itself threw — guard the rollback.
            await RollbackSafeAsync(transaction, dbName, ct).ConfigureAwait(false);

            // 0-based index of the change that actually failed. If the exception fired before
            // the loop body (currentIndex == -1), fall back to 0 for a sane report.
            var failedIndex = currentIndex < 0 ? 0 : currentIndex;
            var failedChange = failedIndex < total ? diff.Changes[failedIndex] : null;
            // Rendering the failed statement is best-effort: a generator may itself throw for the
            // change (e.g. a rebuild-only change reached the per-change path). Reporting the
            // original failure matters more than the SQL snippet, so never let this mask it.
            var failedSql = TryRenderChangeSql(sqlGenerator, failedChange);
            var suggestions = BuildErrorSuggestions(failedChange, ex);

            MigrationDiagnostics.MigrationFailed(_logger, ex, dbName, failedIndex + 1, total,
                failedChange?.Description ?? "unknown");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var errorMsg = new StringBuilder();
            errorMsg.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Migration failed at step {failedIndex + 1}/{total}: {failedChange?.Description ?? "unknown"}"));
            errorMsg.AppendLine($"Error: {ex.Message}");
            if (suggestions.Length > 0)
            {
                errorMsg.AppendLine();
                errorMsg.AppendLine("Suggestions:");
                foreach (var s in suggestions)
                    errorMsg.AppendLine($"  - {s}");
            }

            progressStream?.Report(new MigrationProgressEvent("error",
                $"[{dbName}] Failed at step {failedIndex + 1}/{total}: {failedChange?.Description ?? "unknown"}",
                IsError: true, ErrorDetail: ex.ToString(), DatabaseName: dbName));

            return new MigrationResult(false, 0, sw.Elapsed, fullSql, diff.Changes, errorMsg.ToString())
            {
                FailedChangeIndex = failedIndex,
                FailedChangeSql = failedSql,
                Suggestions = suggestions
            };
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }

        // Phase 4.5: post-commit concurrent indexes (out-of-transaction).
        // PostgreSQL CONCURRENTLY and SQL Server WITH (ONLINE = ON) cannot run inside a
        // transaction. Schema changes are already committed; if a concurrent build fails
        // the index is left INVALID on the server and the migration is reported failed.
        foreach (var addIdx in deferredConcurrent)
        {
            var indexSql = sqlGenerator.GenerateChangeScript(addIdx);
            try
            {
                var cmd = connection.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = indexSql;
                    cmd.CommandTimeout = (int)context.Options.Timeout.TotalSeconds;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                progressStream?.Report(new MigrationProgressEvent("applying",
                    $"[{dbName}] concurrent build OK: {addIdx.Index.Name}", DatabaseName: dbName));
            }
            catch (Exception ex)
            {
                MigrationDiagnostics.MigrationFailed(_logger, ex, dbName, appliedCount, total,
                    $"concurrent build of {addIdx.Index.Name}");
                progressStream?.Report(new MigrationProgressEvent("error",
                    $"[{dbName}] concurrent index '{addIdx.Index.Name}' failed after commit: {ex.Message}",
                    IsError: true, ErrorDetail: ex.ToString(), DatabaseName: dbName));

                return new MigrationResult(false, appliedCount, sw.Elapsed, fullSql, diff.Changes,
                    $"Schema committed but concurrent index '{addIdx.Index.Name}' failed: {ex.Message}. The index is left INVALID on the server — drop and recreate it after fixing the cause.")
                {
                    Suggestions = ImmutableArray.Create(
                        "The schema migration succeeded — only the post-commit concurrent index build failed.",
                        "Drop the failed index (it is INVALID on PostgreSQL) and recreate it after fixing the underlying cause.")
                };
            }
        }

        // Phase 5: Record in audit table
        sw.Stop();
        await SchemaAuditStore.EnsureTableAsync(
            connection, sqlGenerator, ct, context.Options.AuditTableName).ConfigureAwait(false);
        await SchemaAuditStore.RecordMigrationAsync(
            connection, context.DesiredSchema, fullSql, total, sw.ElapsedMilliseconds,
            auditTableName: context.Options.AuditTableName, ct: ct).ConfigureAwait(false);

        // Phase 6: Run seed providers (only when changes were applied)
        if (seedProviders is not null)
        {
            var applicableSeeds = seedProviders
                .Where(s => s.DatabaseName is null || string.Equals(s.DatabaseName, dbName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Order);

            foreach (var seed in applicableSeeds)
            {
                await seed.SeedAsync(connection, ct).ConfigureAwait(false);
            }
        }

        MigrationDiagnostics.MigrationComplete(_logger, dbName, total, sw.ElapsedMilliseconds);
        activity?.SetTag(MigrationTags.DurationMs, sw.ElapsedMilliseconds);

        progressStream?.Report(new MigrationProgressEvent("complete",
            $"[{dbName}] {total} changes applied in {sw.ElapsedMilliseconds}ms",
            ProgressPercent: 100.0, DatabaseName: dbName));

        var schemaResult = new MigrationResult(true, total, sw.Elapsed, fullSql, diff.Changes, null);
        return await ApplyDataMigrationsAsync(connection, sqlGenerator, dbName, sw, ct, schemaResult)
            .ConfigureAwait(false);
    }

}
