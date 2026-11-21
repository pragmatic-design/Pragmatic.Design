using System.Data.Common;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     The <see cref="IRecurringJobStore" /> of the <c>[EnableJobPersistence]</c> boundary the Sql engine serves: what
///     <c>EfCoreRecurringJobStore</c> does, on the same table, through the boundary's session. The tick is claimed with a compare-and-swap on
///     the next occurrence the host read, so one host of many enqueues it.
/// </summary>
public sealed class SqlRecurringJobStore(SqlSession session) : IRecurringJobStore
{
    /// <inheritdoc />
    /// <remarks>
    ///     The declaration's fields are written over an existing definition and its scheduling state is kept (enabled, last and next run), as on
    ///     EF Core. Two hosts starting together both find no row: the one whose insert loses to the other's key updates instead, and a key
    ///     conflict that still finds no row is not a race and is thrown.
    /// </remarks>
    public async Task UpsertAsync(RecurringJobDefinition definition, CancellationToken ct = default)
    {
        if (await UpdateDeclaredAsync(definition, ct).ConfigureAwait(false) > 0)
            return;

        try
        {
            await session.InsertAsync(JobRecords.From(new RecurringJobRow(), definition), ct).ConfigureAwait(false);
        }
        catch (DbException)
        {
            if (await UpdateDeclaredAsync(definition, ct).ConfigureAwait(false) == 0)
                throw;
        }
    }

    private async Task<int> UpdateDeclaredAsync(RecurringJobDefinition definition, CancellationToken ct)
        => await session.Query<RecurringJobRow>().Where((r, id) => r.Id == id, definition.Id)
            .ExecuteUpdateAsync((s, d) => s.SetProperty(r => r.JobType, d.JobType).SetProperty(r => r.CronExpression, d.CronExpression)
                    .SetProperty(r => r.ParametersJson, d.ParametersJson).SetProperty(r => r.ParameterType, d.ParameterType)
                    .SetProperty(r => r.TimeZoneId, d.TimeZoneId).SetProperty(r => r.MisfirePolicy, d.MisfirePolicy),
                (definition.JobType, definition.CronExpression, definition.ParametersJson, definition.ParameterType, definition.TimeZoneId,
                    MisfirePolicy: (int)definition.MisfirePolicy), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringJobDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await session.Query<RecurringJobRow>().Where((r, n) => r.IsEnabled && r.NextExecutionAt != null && r.NextExecutionAt <= n, now)
            .OrderBy(r => r.NextExecutionAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(JobRecords.ToDefinition)];
    }

    /// <inheritdoc />
    public async Task UpdateNextExecutionAsync(string id, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default)
        => await session.Query<RecurringJobRow>().Where((r, i) => r.Id == i, id)
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.NextExecutionAt, a.Next).SetProperty(r => r.LastExecutedAt, a.Last),
                (Next: nextExecution, Last: (DateTimeOffset?)lastExecuted), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TryClaimDueAsync(string id, DateTimeOffset? expectedNextExecution, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted,
        CancellationToken ct = default)
    {
        // Compare-and-swap: advance NextExecutionAt only if it still holds the value this host read. A second host racing the same tick finds
        // the advanced value, matches no row, and does not enqueue. A missing occurrence is its own statement: equal to a null parameter
        // matches nothing in SQL.
        var values = (Next: nextExecution, Last: (DateTimeOffset?)lastExecuted);
        var affected = expectedNextExecution is { } expected
            ? await session.Query<RecurringJobRow>().Where((r, a) => r.Id == a.Id && r.IsEnabled && r.NextExecutionAt == a.Expected, (Id: id, Expected: expected))
                .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.NextExecutionAt, a.Next).SetProperty(r => r.LastExecutedAt, a.Last), values, ct).ConfigureAwait(false)
            : await session.Query<RecurringJobRow>().Where((r, i) => r.Id == i && r.IsEnabled && r.NextExecutionAt == null, id)
                .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.NextExecutionAt, a.Next).SetProperty(r => r.LastExecutedAt, a.Last), values, ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<RecurringJobDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        var row = await session.Query<RecurringJobRow>().FirstOrDefaultAsync((r, i) => r.Id == i, id, ct).ConfigureAwait(false);
        return row is null ? null : JobRecords.ToDefinition(row);
    }

    /// <inheritdoc />
    public async Task DisableAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobRow>().Where((r, i) => r.Id == i, id)
            .ExecuteUpdateAsync((s, enabled) => s.SetProperty(r => r.IsEnabled, enabled), false, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task EnableAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobRow>().Where((r, i) => r.Id == i, id)
            .ExecuteUpdateAsync((s, enabled) => s.SetProperty(r => r.IsEnabled, enabled), true, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobRow>().Where((r, i) => r.Id == i, id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
}
