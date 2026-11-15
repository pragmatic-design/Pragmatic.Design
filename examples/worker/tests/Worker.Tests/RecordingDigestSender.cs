using Worker.Host.Infrastructure.Services;

namespace Worker.Tests;

/// <summary>The test's sender, put in place of the worker's through the factory: completes with the id of the job that sent the digest.</summary>
public sealed class RecordingDigestSender : IDigestSender
{
    private readonly TaskCompletionSource<Guid> _sent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The id of the first job that sent a digest.</summary>
    public Task<Guid> Sent => _sent.Task;

    public Task SendAsync(Guid jobId, CancellationToken ct)
    {
        _sent.TrySetResult(jobId);
        return Task.CompletedTask;
    }
}
