using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Ensure;
using Pragmatic.Jobs;

namespace Pragmatic.Jobs.Configuration;

/// <summary>
///     Fluent builder for configuring Pragmatic.Jobs.
/// </summary>
public sealed class JobsBuilder
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }

    internal readonly JobsOptions Options = new();

    internal JobsBuilder(IServiceCollection services) => Services = services;

    /// <summary>Sets the number of concurrent worker tasks.</summary>
    public JobsBuilder WithWorkerCount(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Worker count must be at least 1.");
        Options.WorkerCount = count;
        return this;
    }

    /// <summary>Sets the polling interval in seconds.</summary>
    public JobsBuilder WithPollingInterval(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Polling interval must be at least 1 second.");
        Options.PollingIntervalSeconds = seconds;
        return this;
    }

    /// <summary>Sets the lease duration in seconds for distributed locking.</summary>
    public JobsBuilder WithLeaseTime(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Lease time must be at least 1 second.");
        Options.LeaseTimeSeconds = seconds;
        return this;
    }

    /// <summary>Sets the default max retry attempts for jobs without [Retry].</summary>
    public JobsBuilder WithMaxRetries(int retries)
    {
        if (retries < 1) throw new ArgumentOutOfRangeException(nameof(retries), retries, "Max retries must be at least 1.");
        Options.DefaultMaxRetries = retries;
        return this;
    }

    /// <summary>Sets the batch size (max pending jobs per poll).</summary>
    public JobsBuilder WithBatchSize(int size)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), size, "Batch size must be at least 1.");
        Options.BatchSize = size;
        return this;
    }

    /// <summary>
    ///     Sets how many days a finished job is kept before being purged. 0 disables purging and
    ///     retains job history — including its serialized parameters — indefinitely.
    /// </summary>
    public JobsBuilder WithRetention(int days)
    {
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(days), days, "Retention days cannot be negative.");
        Options.RetentionDays = days;
        return this;
    }

    /// <summary>
    ///     Sets how far behind its occurrence a recurring job may be before it counts as a misfire
    ///     and triggers its <c>MisfirePolicy</c>. A due job within this window is a normal late run.
    /// </summary>
    public JobsBuilder WithMisfireThreshold(TimeSpan threshold)
    {
        if (threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Misfire threshold must be positive.");
        Options.MisfireThreshold = threshold;
        return this;
    }

    /// <summary>Sets a custom worker ID for lease acquisition.</summary>
    public JobsBuilder WithWorkerId(string workerId)
    {
        // Guarded like every sibling setter: an empty worker id silently becomes the value written
        // to LeasedBy, defeating the lease fencing that identifies which worker owns a job.
        Pragmatic.Ensure.Ensure.ThrowIfNullOrWhiteSpace(workerId);

        Options.WorkerId = workerId;
        return this;
    }

    /// <summary>
    ///     Switches job persistence away from the in-memory stores, which lose every queued job on
    ///     restart and cannot coordinate workers across hosts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This drops any in-memory store already registered, so ordering between this call and
    ///         <c>AddPragmaticJobs()</c> does not matter. Pair it with
    ///         <c>JobsEfCoreExtensions.UseEfCorePersistence()</c> from the <c>Pragmatic.Jobs.EFCore</c>
    ///         package, which registers <c>EfCoreJobStore</c> and <c>EfCoreRecurringJobStore</c>.
    ///     </para>
    ///     <para>
    ///         If no store is registered by the time the host starts, the job processor fails fast
    ///         with an actionable message. Falling back to the in-memory stores silently would ship an
    ///         app that asked for durable jobs without them.
    ///     </para>
    /// </remarks>
    public JobsBuilder UseEfCore()
    {
        Options.UseEfCore = true;

        // Remove rather than merely flag: AddPragmaticJobs() may already have run (the Composition
        // host registers module defaults before the user's UseJobs callback), and its TryAdd would
        // otherwise have locked the in-memory stores in permanently.
        Services.RemoveAll<IJobStore>();
        Services.RemoveAll<IRecurringJobStore>();

        return this;
    }
}
