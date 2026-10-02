using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Showcase.Booking.Infrastructure.Jobs;

/// <summary>
///     Puts back on sale the rooms held by the reservations the detection pass just marked as
///     no-shows. It is the second half of that hourly pass, and it only makes sense after it.
/// </summary>
/// <remarks>
///     <para>
///         Declared as the continuation of <see cref="NoShowDetectionJob"/> rather than scheduled by
///         it: the two are separate rows, so this one gets its own attempt budget — the
///         <c>[Retry]</c> below, not the detection job's — and a release that fails is retried
///         without detecting anything twice.
///     </para>
///     <para>
///         Like the job it follows, the body is deliberately empty: what these two demonstrate is the
///         chaining, and a real release would go through the boundary actions and the repository.
///     </para>
/// </remarks>
[Job]
[Retry(MaxAttempts = 3)]
public sealed partial class ReleaseNoShowRoomsJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}
