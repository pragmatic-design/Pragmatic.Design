using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     The <see cref="IRecurringJobStore" /> of the <c>[EnableJobPersistence]</c> boundary the documents engine serves: what
///     <c>EfCoreRecurringJobStore</c> does, through the boundary's session. The tick is claimed with a compare-and-swap on the next occurrence
///     the host read, so one host of many enqueues it.
/// </summary>
public sealed class DocRecurringJobStore(DocSession session) : IRecurringJobStore
{
    /// <inheritdoc />
    /// <remarks>
    ///     The declaration's fields are written over an existing definition and its scheduling state is kept, as on EF Core. Two hosts starting
    ///     together both find no document: the one whose insert loses to the other's key (MongoDB's duplicate key, Cosmos DB's conflict)
    ///     updates instead, and a conflict that still finds no document is not a race and is thrown.
    /// </remarks>
    public async Task UpsertAsync(RecurringJobDefinition definition, CancellationToken ct = default)
    {
        if (await UpdateDeclaredAsync(definition, ct).ConfigureAwait(false) > 0)
            return;

        try
        {
            await session.InsertAsync(JobRecords.From(new RecurringJobDocument(), definition), ct).ConfigureAwait(false);
        }
        catch (Exception taken) when (taken is MongoWriteException { IsDuplicateKey: true } or CosmosException { IsConflict: true })
        {
            if (await UpdateDeclaredAsync(definition, ct).ConfigureAwait(false) == 0)
                throw;
        }
    }

    private async Task<long> UpdateDeclaredAsync(RecurringJobDefinition definition, CancellationToken ct)
        => await session.Query<RecurringJobDocument>().Where((d, id) => d.Id == id, definition.Id)
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.JobType, a.JobType).Set(d => d.CronExpression, a.CronExpression).Set(d => d.ParametersJson, a.ParametersJson)
                    .Set(d => d.ParameterType, a.ParameterType).Set(d => d.TimeZoneId, a.TimeZoneId).Set(d => d.MisfirePolicy, a.MisfirePolicy),
                (definition.JobType, definition.CronExpression, definition.ParametersJson, definition.ParameterType, definition.TimeZoneId,
                    MisfirePolicy: (int)definition.MisfirePolicy), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringJobDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var documents = await session.Query<RecurringJobDocument>().Where((d, n) => d.IsEnabled && d.NextExecutionAt != null && d.NextExecutionAt <= n, now.UtcTicks)
            .OrderBy(d => d.NextExecutionAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. documents.Select(JobRecords.ToDefinition)];
    }

    /// <inheritdoc />
    public async Task UpdateNextExecutionAsync(string id, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default)
        => await session.Query<RecurringJobDocument>().Where((d, i) => d.Id == i, id)
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.NextExecutionAt, a.Next).Set(d => d.LastExecutedAt, a.Last),
                (Next: nextExecution?.UtcTicks, Last: (long?)lastExecuted.UtcTicks), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TryClaimDueAsync(string id, DateTimeOffset? expectedNextExecution, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted,
        CancellationToken ct = default)
    {
        // Compare-and-swap: advance NextExecutionAt only if it still holds the value this host read; a missing occurrence is its own filter.
        var values = (Next: nextExecution?.UtcTicks, Last: (long?)lastExecuted.UtcTicks);
        var affected = expectedNextExecution is { } expected
            ? await session.Query<RecurringJobDocument>().Where((d, a) => d.Id == a.Id && d.IsEnabled && d.NextExecutionAt == a.Expected, (Id: id, Expected: expected.UtcTicks))
                .ExecuteUpdateAsync((s, a) => s.Set(d => d.NextExecutionAt, a.Next).Set(d => d.LastExecutedAt, a.Last), values, ct).ConfigureAwait(false)
            : await session.Query<RecurringJobDocument>().Where((d, i) => d.Id == i && d.IsEnabled && d.NextExecutionAt == null, id)
                .ExecuteUpdateAsync((s, a) => s.Set(d => d.NextExecutionAt, a.Next).Set(d => d.LastExecutedAt, a.Last), values, ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<RecurringJobDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        var document = await session.Query<RecurringJobDocument>().FirstOrDefaultAsync((d, i) => d.Id == i, id, ct).ConfigureAwait(false);
        return document is null ? null : JobRecords.ToDefinition(document);
    }

    /// <inheritdoc />
    public async Task DisableAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobDocument>().Where((d, i) => d.Id == i, id)
            .ExecuteUpdateAsync((s, enabled) => s.Set(d => d.IsEnabled, enabled), false, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task EnableAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobDocument>().Where((d, i) => d.Id == i, id)
            .ExecuteUpdateAsync((s, enabled) => s.Set(d => d.IsEnabled, enabled), true, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
        => await session.Query<RecurringJobDocument>().Where((d, i) => d.Id == i, id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
}
