using Microsoft.Extensions.Logging;
using Pragmatic.ControlPlane;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Default implementation: iterates active tenants with dedicated databases
///     and runs <see cref="IMigrationRunner"/> for each, honouring
///     <see cref="TenantMigrationOptions"/>.
/// </summary>
public sealed class TenantMigrationOrchestrator(
    ITenantStore tenantStore,
    IMigrationRunner migrationRunner,
    IHostStatus? hostStatus = null,
    IControlPlane? controlPlane = null,
    ILogger<TenantMigrationOrchestrator>? logger = null,
    TenantMigrationOptions? options = null) : ITenantMigrationOrchestrator
{
    private readonly TenantMigrationOptions _options = options ?? new TenantMigrationOptions();

    /// <inheritdoc />
    public async Task<TenantMigrationSummary> MigrateAllTenantsAsync(
        SchemaVersion desiredSchema,
        MigrationOptions migrationOptions,
        CancellationToken ct = default)
    {
        var allTenants = await tenantStore.GetActiveAsync(ct).ConfigureAwait(false);
        var dedicatedTenants = allTenants
            .Where(t => !string.IsNullOrEmpty(t.ConnectionString))
            .ToList();

        // ⚠️ The sweep visits ACTIVE tenants, so one still provisioning, suspended, or left Migrating by
        // an earlier failure has a database that exists and is never looked at. Correct — its own
        // onboarding migrates it — but it has to be said: a run across N customers accounts for N
        // databases, not for however many it reached.
        var notVisited = await NotActiveWithADatabaseAsync(ct).ConfigureAwait(false);

        if (dedicatedTenants.Count == 0)
        {
            logger?.LogInformation("No tenants with dedicated databases found — skipping tenant migration");
            return new TenantMigrationSummary
            {
                TotalTenants = 0, SuccessCount = 0, FailureCount = 0, SkippedCount = 0, Results = [],
                NotVisited = notVisited,
            };
        }

        logger?.LogInformation("Starting tenant migration for {Count} dedicated databases", dedicatedTenants.Count);

        // Report migrating state
        hostStatus?.TransitionTo(HostState.Migrating, $"Migrating {dedicatedTenants.Count} tenant databases");
        if (controlPlane is not null)
            await controlPlane.ReportStatusAsync(ct).ConfigureAwait(false);

        var run = _options.MaxParallelism > 1
            ? await RunInParallelAsync(dedicatedTenants, desiredSchema, migrationOptions, ct).ConfigureAwait(false)
            : await RunSequentiallyAsync(dedicatedTenants, desiredSchema, migrationOptions, ct).ConfigureAwait(false);

        // Tenants that never produced a result (a stop-on-failure or a cancellation cut the run
        // short) are skipped, not failed.
        var skippedCount = dedicatedTenants.Count - run.Results.Count;

        // Only return to Ready when every tenant succeeded. On failure or cancellation the host
        // is left in a degraded (Maintenance) state so the operator can investigate/retry.
        if (run.FailureCount == 0 && skippedCount == 0)
        {
            hostStatus?.TransitionTo(HostState.Ready);
        }
        else
        {
            var reason = run.Cancelled
                ? $"Tenant migration cancelled: {run.SuccessCount} succeeded, {run.FailureCount} failed, {skippedCount} skipped"
                : $"Tenant migration incomplete: {run.FailureCount} of {dedicatedTenants.Count} tenant(s) failed";
            hostStatus?.TransitionTo(HostState.Maintenance, reason);
        }

        if (controlPlane is not null)
            await controlPlane.ReportStatusAsync(ct).ConfigureAwait(false);

        // Surface cancellation to the caller without discarding the partial summary's bookkeeping.
        ct.ThrowIfCancellationRequested();

        // The ones the run itself left behind, named rather than only counted: "3 of 5 skipped" leaves
        // an operator with no way to know which three.
        var attempted = run.Results.Select(r => r.TenantId).ToHashSet(StringComparer.Ordinal);
        var cutShort = dedicatedTenants
            .Where(t => !attempted.Contains(t.TenantId))
            .Select(t => new TenantNotVisited(
                t.TenantId, t.ConnectionString,
                "the run stopped before reaching it (a failure with ContinueOnFailure off, or a cancellation)"));

        return new TenantMigrationSummary
        {
            TotalTenants = dedicatedTenants.Count,
            SuccessCount = run.SuccessCount,
            FailureCount = run.FailureCount,
            SkippedCount = skippedCount,
            Results = run.Results,
            NotVisited = [.. notVisited, .. cutShort],
        };
    }

    /// <summary>
    ///     Tenants that have a database of their own and are not active, so the sweep never sees them.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Asked of <c>GetAllAsync</c> and not of the active list, which is the whole point: this is
    ///     the set the sweep's own query removed. A tenant on the <b>shared</b> database is not here —
    ///     it has nothing of its own to migrate, so its absence is not an absence.
    /// </remarks>
    private async Task<IReadOnlyList<TenantNotVisited>> NotActiveWithADatabaseAsync(CancellationToken ct)
    {
        var all = await tenantStore.GetAllAsync(ct).ConfigureAwait(false);

        return
        [
            .. all
                .Where(t => !string.IsNullOrEmpty(t.ConnectionString) && t.State != TenantState.Active)
                .Select(t => new TenantNotVisited(
                    t.TenantId, t.ConnectionString,
                    $"the tenant is {t.State}, and the sweep visits active ones"))
        ];
    }

    /// <inheritdoc />
    public async Task<MigrationResult> MigrateTenantAsync(
        string tenantId,
        SchemaVersion desiredSchema,
        MigrationOptions migrationOptions,
        CancellationToken ct = default)
    {
        var tenant = await tenantStore.GetByIdAsync(tenantId, ct).ConfigureAwait(false);
        if (tenant is null)
            return new MigrationResult(false, 0, TimeSpan.Zero, null, [], $"Tenant '{tenantId}' not found.");

        if (string.IsNullOrEmpty(tenant.ConnectionString))
            return MigrationResult.NoChanges; // Shared DB — not managed per-tenant

        return await MigrateTenantCoreAsync(tenant, desiredSchema, migrationOptions, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Sequential sweep (<c>MaxParallelism = 1</c>, the default). It is the only mode that can
    ///     honour <see cref="TenantMigrationOptions.ContinueOnFailure" /> = false: stopping before
    ///     the next tenant only means something when tenants run one after another.
    /// </summary>
    private async Task<RunOutcome> RunSequentiallyAsync(
        List<TenantInfo> tenants, SchemaVersion desiredSchema, MigrationOptions migrationOptions, CancellationToken ct)
    {
        var results = new List<TenantMigrationResult>();
        var successCount = 0;
        var failureCount = 0;
        var cancelled = false;

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var (result, wasCancelled) = await MigrateOneAsync(tenant, desiredSchema, migrationOptions, ct)
                .ConfigureAwait(false);

            if (wasCancelled)
            {
                cancelled = true;
                break;
            }

            results.Add(result!);
            if (result!.Result.Success) successCount++;
            else failureCount++;

            await ReportProgressAsync(tenant.TenantId, tenants.Count, results.Count, result.Result, ct)
                .ConfigureAwait(false);

            if (!result.Result.Success && !_options.ContinueOnFailure)
            {
                logger?.LogError(
                    "Stopping tenant migration: {TenantId} failed and ContinueOnFailure is off — {Remaining} tenant(s) not attempted",
                    tenant.TenantId, tenants.Count - results.Count);
                break;
            }
        }

        return new RunOutcome(results, successCount, failureCount, cancelled);
    }

    /// <summary>
    ///     Bounded-parallel sweep, capped at <see cref="TenantMigrationOptions.MaxParallelism" />.
    ///     Every tenant is attempted: with migrations already in flight there is no coherent "stop
    ///     before the next one", so <c>ContinueOnFailure</c> does not apply here — which is exactly
    ///     why sequential is the default.
    /// </summary>
    private async Task<RunOutcome> RunInParallelAsync(
        List<TenantInfo> tenants, SchemaVersion desiredSchema, MigrationOptions migrationOptions, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(_options.MaxParallelism);
        var completed = 0;
        var cancelled = 0;
        // Indexed by tenant position so the summary keeps the tenant order regardless of the order
        // the parallel migrations happen to finish in.
        var ordered = new TenantMigrationResult?[tenants.Count];

        var tasks = tenants.Select(async (tenant, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var (result, wasCancelled) = await MigrateOneAsync(tenant, desiredSchema, migrationOptions, ct)
                    .ConfigureAwait(false);

                if (wasCancelled)
                {
                    Interlocked.Exchange(ref cancelled, 1);
                    return;
                }

                ordered[index] = result;
                var done = Interlocked.Increment(ref completed);
                await ReportProgressAsync(tenant.TenantId, tenants.Count, done, result!.Result, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Interlocked.Exchange(ref cancelled, 1);
        }

        var results = ordered.Where(r => r is not null).Select(r => r!).ToList();
        return new RunOutcome(
            results,
            results.Count(r => r.Result.Success),
            results.Count(r => !r.Result.Success),
            Volatile.Read(ref cancelled) == 1);
    }

    /// <summary>
    ///     Migrates one tenant and moves it to its resulting state. Never throws for a tenant-level
    ///     failure: an unexpected exception becomes a failed <see cref="MigrationResult" /> so the
    ///     summary keeps counting, and the caller decides whether to continue.
    /// </summary>
    private async Task<(TenantMigrationResult? Result, bool Cancelled)> MigrateOneAsync(
        TenantInfo tenant,
        SchemaVersion desiredSchema,
        MigrationOptions migrationOptions,
        CancellationToken ct)
    {
        try
        {
            logger?.LogInformation("Migrating tenant {TenantId} ({TenantName})", tenant.TenantId, tenant.TenantName);

            await tenantStore.UpdateAsync(tenant with { State = TenantState.Migrating }, ct).ConfigureAwait(false);

            // TenantTimeout bounds a single tenant so one unreachable database cannot stall the
            // whole sweep. Linked to the caller's token, so an outer cancellation still wins.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.TenantTimeout);

            MigrationResult result;
            try
            {
                result = await MigrateTenantCoreAsync(tenant, desiredSchema, migrationOptions, timeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The tenant ran out of time — a failure of THIS tenant, not of the whole run.
                result = new MigrationResult(false, 0, _options.TenantTimeout, null, [],
                    $"Tenant migration exceeded TenantTimeout ({_options.TenantTimeout}).");
            }

            if (result.Success)
            {
                await tenantStore.UpdateAsync(tenant with { State = TenantState.Active }, ct).ConfigureAwait(false);
                logger?.LogInformation("Tenant {TenantId} migration completed: {Changes} changes applied",
                    tenant.TenantId, result.ChangesApplied);
            }
            else
            {
                await MarkFailedAsync(tenant, ct).ConfigureAwait(false);
                logger?.LogError("Tenant {TenantId} migration failed: {Error}", tenant.TenantId, result.Error);
            }

            return (new TenantMigrationResult(tenant.TenantId, result), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled mid-flight: not a failure. The tenant stays Migrating for a later retry.
            return (null, true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error migrating tenant {TenantId}", tenant.TenantId);
            await MarkFailedAsync(tenant, ct).ConfigureAwait(false);
            return (new TenantMigrationResult(tenant.TenantId,
                new MigrationResult(false, 0, TimeSpan.Zero, null, [], ex.Message)), false);
        }
    }

    /// <summary>
    ///     Applies the failure state. With <see cref="TenantMigrationOptions.SuspendOnFailure" />
    ///     off the tenant is left <see cref="TenantState.Migrating" /> rather than suspended, so a
    ///     retry can pick it up without an operator un-suspending it first.
    /// </summary>
    private async Task MarkFailedAsync(TenantInfo tenant, CancellationToken ct)
    {
        if (!_options.SuspendOnFailure) return;
        await tenantStore.UpdateAsync(tenant with { State = TenantState.Suspended }, ct).ConfigureAwait(false);
    }

    private async Task ReportProgressAsync(
        string tenantId, int totalTenants, int completed, MigrationResult result, CancellationToken ct)
    {
        var progress = new MigrationStatus(
            $"tenant:{tenantId}",
            totalTenants,
            completed,
            (double)completed / totalTenants * 100,
            !result.Success,
            result.Error);

        hostStatus?.UpdateMigrationProgress(progress);
        if (controlPlane is not null)
            await controlPlane.ReportStatusAsync(ct).ConfigureAwait(false);
    }

    private async Task<MigrationResult> MigrateTenantCoreAsync(
        TenantInfo tenant,
        SchemaVersion desiredSchema,
        MigrationOptions migrationOptions,
        CancellationToken ct)
    {
        var context = new MigrationContext(tenant.ConnectionString!, desiredSchema, migrationOptions);
        return await migrationRunner.MigrateAsync(context, ct).ConfigureAwait(false);
    }

    private sealed record RunOutcome(
        List<TenantMigrationResult> Results,
        int SuccessCount,
        int FailureCount,
        bool Cancelled);
}
