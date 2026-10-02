namespace Pragmatic.Jobs;

/// <summary>
///     Schedules jobs for execution — immediate, delayed, or at a specific time.
/// </summary>
/// <remarks>
///     <para>
///         <b><see cref="ScheduleAsync{TJob}(TimeSpan?, string?, JobContinuation?, CancellationToken)"/> vs
///         <see cref="ScheduleAtAsync{TJob}(DateTimeOffset, string?, JobContinuation?, CancellationToken)"/></b>:
///         <see cref="ScheduleAsync{TJob}"/> takes an OFFSET from "now" — pass <c>null</c>
///         to run immediately or a <see cref="TimeSpan"/> to delay; the scheduler computes
///         <c>now + delay</c> at the time of the call. <see cref="ScheduleAtAsync{TJob}"/>
///         takes an ABSOLUTE timestamp — useful when the moment is known up-front
///         (e.g. "first business day of next month at 09:00 UTC") and must not drift if
///         enqueue is itself delayed. Both share the same retry / correlation semantics.
///     </para>
///     <para>
///         Absolute times are normalized to UTC before persisting. A non-UTC offset would otherwise
///         be rejected outright by Npgsql (<c>timestamptz</c> requires offset 0) while SQL Server
///         accepted it, and would corrupt SQLite's lexicographic timestamp ordering.
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IJobScheduler
{
    /// <summary>
    ///     Schedules a parameterless job (immediate, or after <paramref name="delay"/>). An optional
    ///     <see cref="JobContinuation" /> is persisted with the job and enqueued once it completes, so
    ///     the chain survives a restart between the two.
    /// </summary>
    Task<Guid> ScheduleAsync<TJob>(
        TimeSpan? delay = null,
        string? correlationId = null,
        JobContinuation? continuation = null,
        CancellationToken ct = default) where TJob : IJob;

    /// <summary>
    ///     Schedules a job with typed parameters, optionally followed by a <see cref="JobContinuation" />.
    /// </summary>
    Task<Guid> ScheduleAsync<TJob, TParams>(
        TParams parameters,
        TimeSpan? delay = null,
        string? correlationId = null,
        JobContinuation? continuation = null,
        CancellationToken ct = default)
        where TJob : IJob<TParams>
        where TParams : notnull;

    /// <summary>
    ///     Schedules a parameterless job at a specific time, optionally followed by a
    ///     <see cref="JobContinuation" />.
    /// </summary>
    Task<Guid> ScheduleAtAsync<TJob>(
        DateTimeOffset scheduledFor,
        string? correlationId = null,
        JobContinuation? continuation = null,
        CancellationToken ct = default) where TJob : IJob;

    /// <summary>
    ///     Schedules a job with typed parameters at a specific time, optionally followed by a
    ///     <see cref="JobContinuation" />.
    /// </summary>
    Task<Guid> ScheduleAtAsync<TJob, TParams>(
        TParams parameters,
        DateTimeOffset scheduledFor,
        string? correlationId = null,
        JobContinuation? continuation = null,
        CancellationToken ct = default)
        where TJob : IJob<TParams>
        where TParams : notnull;

    /// <summary>Cancels a pending job.</summary>
    Task CancelAsync(Guid jobId, CancellationToken ct = default);
}
