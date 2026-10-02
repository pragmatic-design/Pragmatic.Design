namespace Pragmatic.Jobs;

/// <summary>
///     Persists a recurring job definition, computing its first execution time from the cron
///     expression when the definition has never run.
/// </summary>
/// <remarks>
///     Cron evaluation lives here rather than in <see cref="IRecurringJobStore"/> implementations so
///     that stores stay pure persistence and every store — including custom ones — inherits correct
///     bootstrapping. A definition persisted without <c>NextExecutionAt</c> is never due, so this
///     step is what makes a <c>[RecurringJob]</c> fire at all.
/// </remarks>
public interface IRecurringJobRegistrar
{
    /// <summary>
    ///     Upserts <paramref name="definition"/>, seeding <c>NextExecutionAt</c> from its cron
    ///     expression when neither the incoming definition nor the persisted row carries one.
    ///     Returns <c>false</c> when the definition was skipped because its cron expression or
    ///     timezone could not be parsed.
    /// </summary>
    Task<bool> RegisterAsync(RecurringJobDefinition definition, CancellationToken ct = default);
}
