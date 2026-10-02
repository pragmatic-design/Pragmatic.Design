using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Demonstrates the lease-based distributed lock that lets many
///     JobProcessorService instances (across processes / machines) safely drain
///     one shared queue without double-executing a job.
///     <para>
///         The lock contract lives on <see cref="IJobStore"/>:
///         <c>TryAcquireLeaseAsync</c> is atomic (exactly one worker wins),
///         <c>RenewLeaseAsync</c> extends a held lease, and
///         <c>ReleaseExpiredLeasesAsync</c> reclaims jobs from crashed workers.
///         This sample drives those methods directly on the in-memory store, which
///         implements the identical semantics via a lock. The production
///         <c>EfCoreJobStore</c> implements the same contract with an atomic
///         <c>ExecuteUpdateAsync</c> (a single conditional UPDATE) — but that query
///         path is not translatable by SQLite, so a live EF run needs PostgreSQL /
///         SQL Server. The behaviour observed here is what the EF store guarantees
///         at the database level.
///     </para>
/// </summary>
public static class DistributedLockingSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Distributed locking (lease contention + expiry) ---");

        var store = new InMemoryJobStore(SystemClock.Instance);

        var enqueued = await store.EnqueueAsync(new JobInstance
        {
            JobType = typeof(GenerateInvoiceJob).FullName!,
            Status = JobStatus.Pending,
            ScheduledFor = DateTimeOffset.UtcNow,
            MaxAttempts = 3,
        });
        var jobId = enqueued.Id;
        Console.WriteLine($"  enqueued shared job     : {jobId}");

        // --- Round 1: two workers race for the same short (2s) lease. ---
        var lease = TimeSpan.FromSeconds(2);
        var raceA = store.TryAcquireLeaseAsync(jobId, "worker-A", lease);
        var raceB = store.TryAcquireLeaseAsync(jobId, "worker-B", lease);
        await Task.WhenAll(raceA, raceB);

        Console.WriteLine($"  worker-A acquired lease : {raceA.Result}");
        Console.WriteLine($"  worker-B acquired lease : {raceB.Result}");
        Console.WriteLine($"  exactly one winner      : {raceA.Result ^ raceB.Result}");

        // A third worker cannot steal a still-valid lease.
        var stealAttempt = await store.TryAcquireLeaseAsync(jobId, "worker-C", lease);
        Console.WriteLine($"  worker-C steal (active) : {stealAttempt} (expected False)");

        // --- Round 2: lease expires, a fresh worker reclaims the job. ---
        Console.WriteLine("  waiting for lease to expire...");
        await Task.Delay(lease + TimeSpan.FromMilliseconds(500));

        // Sweep expired leases back to Pending (what JobProcessorService does each poll).
        await store.ReleaseExpiredLeasesAsync(DateTimeOffset.UtcNow);
        var reclaimed = await store.TryAcquireLeaseAsync(jobId, "worker-C", lease);
        Console.WriteLine($"  worker-C reclaimed lease: {reclaimed} (after expiry recovery)");

        Console.WriteLine();
    }
}
