using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     EF Core-backed batch progress store. Uses atomic <c>ExecuteUpdateAsync</c>
///     for increment operations (safe under concurrency).
/// </summary>
public sealed partial class EfCoreBatchProgressStore(
    DbContext dbContext,
    ILogger<EfCoreBatchProgressStore> logger,
    ITenantContext? tenantContext = null) : IBatchProgressStore
{
    /// <inheritdoc />
    public async Task CreateAsync(BatchProgress progress, CancellationToken ct = default)
    {
        // Stamp the ambient tenant so the row is owned by the current tenant and matches the fail-closed
        // tenant query filter on read. Empty in single-tenant hosts (no filter emitted).
        if (string.IsNullOrEmpty(progress.TenantId))
            progress.TenantId = tenantContext?.TenantId ?? string.Empty;

        // A zero-item batch is already complete on creation: stamp CompletedAt so "active =
        // CompletedAt is null" holds (no increment will ever fire to complete it), matching the
        // in-memory store. Otherwise it would stay active forever.
        if (progress.IsComplete && progress.CompletedAt is null)
            progress.CompletedAt = DateTimeOffset.UtcNow;

        dbContext.Set<BatchProgress>().Add(progress);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        LogBatchCreated(progress.BatchId, progress.Total, progress.Label);
    }

    /// <inheritdoc />
    public async Task IncrementCompletedAsync(Guid batchId, CancellationToken ct = default)
    {
        var updated = await dbContext.Set<BatchProgress>()
            .Where(b => b.BatchId == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Completed, b => b.Completed + 1), ct)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            LogBatchNotFound(batchId);
            return;
        }

        await StampCompletedIfDoneAsync(batchId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task IncrementFailedAsync(Guid batchId, CancellationToken ct = default)
    {
        var updated = await dbContext.Set<BatchProgress>()
            .Where(b => b.BatchId == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Failed, b => b.Failed + 1), ct)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            LogBatchNotFound(batchId);
            return;
        }

        await StampCompletedIfDoneAsync(batchId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stamps <c>CompletedAt</c> once the counters reach <c>Total</c>. The completion condition lives in
    ///     the WHERE clause (not inside SetProperty): a conditional VALUE inside SetProperty does not
    ///     translate on SQLite, whereas a conditional WHERE + a constant SET does on every provider.
    /// </summary>
    private Task StampCompletedIfDoneAsync(Guid batchId, CancellationToken ct)
        => dbContext.Set<BatchProgress>()
            .Where(b => b.BatchId == batchId && b.CompletedAt == null && b.Completed + b.Failed >= b.Total)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.CompletedAt, DateTimeOffset.UtcNow), ct);

    /// <inheritdoc />
    public async Task<BatchProgress?> GetProgressAsync(Guid batchId, CancellationToken ct = default)
    {
        return await dbContext.Set<BatchProgress>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SetDispatchedCountAsync(Guid batchId, int dispatchedCount, CancellationToken ct = default)
        => dbContext.Set<BatchProgress>()
            .Where(b => b.BatchId == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.DispatchedCount, dispatchedCount), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BatchProgress>> GetActiveAsync(CancellationToken ct = default)
    {
        // Canonical "active" = CompletedAt is null (matches InMemoryBatchProgressStore). Order client-side:
        // SQLite cannot translate ORDER BY on a DateTimeOffset column (a real query would run on
        // Postgres/SQL Server, but the store must stay provider-portable, as the outbox does).
        // Cross-tenant by design (monitoring/admin): bypass the fail-closed tenant filter — this runs with
        // no ambient tenant. Per-batch reads (GetProgressAsync) stay tenant-scoped.
        var active = await dbContext.Set<BatchProgress>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.CompletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return active.OrderBy(b => b.StartedAt).ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Batch {BatchId} created: {Total} items, label={Label}")]
    partial void LogBatchCreated(Guid batchId, int total, string? label);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Batch {BatchId} not found for progress update")]
    partial void LogBatchNotFound(Guid batchId);
}
