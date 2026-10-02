namespace Pragmatic.Jobs;

/// <summary>
///     Persistence for recurring job definitions.
///     The SG-generated registration upserts definitions at startup.
/// </summary>
public interface IRecurringJobStore
{
    /// <summary>Inserts or updates a recurring job definition.</summary>
    Task UpsertAsync(RecurringJobDefinition definition, CancellationToken ct = default);

    /// <summary>Gets recurring jobs that are due (NextExecutionAt &lt;= now and enabled).</summary>
    Task<IReadOnlyList<RecurringJobDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Updates the last/next execution timestamps after a job is enqueued.</summary>
    Task UpdateNextExecutionAsync(string id, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default);

    /// <summary>
    ///     Atomically claims a due recurring job by advancing its <c>NextExecutionAt</c>, but only
    ///     if it still matches <paramref name="expectedNextExecution"/> (compare-and-swap). Returns
    ///     <c>true</c> only for the single host that wins the claim, so the job is enqueued exactly
    ///     once per tick across a multi-host deployment. Mirrors the lease pattern used for job execution.
    /// </summary>
    Task<bool> TryClaimDueAsync(string id, DateTimeOffset? expectedNextExecution, DateTimeOffset? nextExecution, DateTimeOffset lastExecuted, CancellationToken ct = default);

    /// <summary>Gets a recurring job by ID.</summary>
    Task<RecurringJobDefinition?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Disables a recurring job (stops scheduling).</summary>
    Task DisableAsync(string id, CancellationToken ct = default);

    /// <summary>Enables a recurring job.</summary>
    Task EnableAsync(string id, CancellationToken ct = default);

    /// <summary>Deletes a recurring job definition.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}
