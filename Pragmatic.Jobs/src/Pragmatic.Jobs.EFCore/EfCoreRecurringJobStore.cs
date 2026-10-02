using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Jobs.EFCore;

/// <summary>
///     EF Core-backed recurring job store.
/// </summary>
public sealed class EfCoreRecurringJobStore(DbContext dbContext) : IRecurringJobStore
{
    /// <inheritdoc />
    /// <remarks>
    ///     Inside the context's execution strategy: the context is the application's, and a generated host
    ///     configures one that retries, which refuses a transaction opened outside it. A retry reads again and
    ///     finds the instance the failed attempt tracked, so writing the same values twice is harmless.
    /// </remarks>
    public Task UpsertAsync(RecurringJobDefinition definition, CancellationToken ct = default)
        => dbContext.Database.CreateExecutionStrategy()
            .ExecuteAsync(token => UpsertOnceAsync(definition, token), ct);

    private async Task UpsertOnceAsync(RecurringJobDefinition definition, CancellationToken ct)
    {
        // Wrap in an explicit transaction so that the read-then-write is atomic.
        // Without this, two concurrent callers can both read null and both attempt Add,
        // causing a primary-key violation.
        var tx = await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await dbContext.Set<RecurringJobDefinition>()
                .FirstOrDefaultAsync(d => d.Id == definition.Id, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                dbContext.Set<RecurringJobDefinition>().Add(definition);
            }
            else
            {
                existing.JobType = definition.JobType;
                existing.CronExpression = definition.CronExpression;
                existing.ParametersJson = definition.ParametersJson;
                existing.ParameterType = definition.ParameterType;
                existing.TimeZoneId = definition.TimeZoneId;
                // MisfirePolicy is a compile-time declaration, so a changed attribute takes effect
                // on restart — unlike the runtime scheduling state below.
                existing.MisfirePolicy = definition.MisfirePolicy;
                // Preserve enabled/disabled state and last execution
            }

            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringJobDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.IsEnabled && d.NextExecutionAt != null && d.NextExecutionAt <= now)
            .OrderBy(d => d.NextExecutionAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateNextExecutionAsync(string id, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default)
    {
        await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.NextExecutionAt, nextExecution)
                .SetProperty(d => d.LastExecutedAt, lastExecuted), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimDueAsync(
        string id,
        DateTimeOffset? expectedNextExecution,
        DateTimeOffset? nextExecution,
        DateTimeOffset lastExecuted,
        CancellationToken ct = default)
    {
        // Compare-and-swap: advance NextExecutionAt only if it still equals the value this
        // host observed in GetDueAsync. A second host racing the same tick sees the already-advanced
        // value, matches zero rows, and skips enqueue — preventing duplicate job instances.
        var affected = await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.Id == id && d.IsEnabled && d.NextExecutionAt == expectedNextExecution)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.NextExecutionAt, nextExecution)
                .SetProperty(d => d.LastExecutedAt, lastExecuted), ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<RecurringJobDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.Set<RecurringJobDefinition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisableAsync(string id, CancellationToken ct = default)
    {
        await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsEnabled, false), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnableAsync(string id, CancellationToken ct = default)
    {
        await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsEnabled, true), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await dbContext.Set<RecurringJobDefinition>()
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
