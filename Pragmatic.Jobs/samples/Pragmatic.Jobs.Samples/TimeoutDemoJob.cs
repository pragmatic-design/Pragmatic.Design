using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     A job wired with <c>[Timeout(TimeoutSeconds = 1)]</c> that tries to run for
///     ~10 seconds. The SG-generated invoker wraps the call in a linked
///     <c>CancellationTokenSource</c> with <c>CancelAfter(1s)</c>, so the
///     <see cref="CancellationToken"/> passed to <see cref="ExecuteAsync"/> is
///     cancelled at the 1-second mark. A well-behaved job observes the token and
///     stops — this demo records that the timeout actually fired.
/// </summary>
[Job]
[Timeout(TimeoutSeconds = 1)]
public sealed partial class TimeoutDemoJob : IJob
{
    public static volatile bool TimedOut;
    public static volatile int LoopsCompleted;

    public async Task ExecuteAsync(JobContext context, CancellationToken ct = default)
    {
        TimedOut = false;
        LoopsCompleted = 0;

        try
        {
            // Pretend to do long work in 500ms slices, honouring the token.
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                LoopsCompleted++;
            }
        }
        catch (OperationCanceledException)
        {
            // The [Timeout] linked token fired before the work finished.
            TimedOut = true;
            throw; // surface cancellation to the runtime
        }
    }
}
