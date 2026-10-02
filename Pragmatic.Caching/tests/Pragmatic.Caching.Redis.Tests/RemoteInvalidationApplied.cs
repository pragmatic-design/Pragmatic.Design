using System.Diagnostics;
using Pragmatic.Caching.Diagnostics;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Caching.Redis.Tests;

/// <summary>
///     Completes when a given node has applied a broadcast for a given tag or key.
/// </summary>
/// <remarks>
///     Pub/sub is asynchronous: the node that invalidates returns before the others have heard. Waiting
///     a fixed time would make every assertion after it also an assertion about the machine's load, so
///     this waits on the event the subscriber emits for each message it applies —
///     <see cref="CacheInvalidationSubscriber.ActivityName" />, tagged with the node that applied it.
///     Create it <b>before</b> the invalidation, so the event cannot come first.
/// </remarks>
public sealed class RemoteInvalidationApplied : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly TaskCompletionSource _applied = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RemoteInvalidationApplied(string nodeId, string tagOrKey)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CachingDiagnostics.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == CacheInvalidationSubscriber.ActivityName
                    && Equals(activity.GetTagItem(CacheTags.Node), nodeId)
                    && (Equals(activity.GetTagItem(CacheTags.Tags), tagOrKey)
                        || Equals(activity.GetTagItem(CacheTags.Key), tagOrKey)))
                    _applied.TrySetResult();
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>The application, or a timeout that names what did not arrive.</summary>
    public Task WaitAsync() => _applied.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Dispose() => _listener.Dispose();
}
