using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Worker.Host.Infrastructure.Services;

namespace Worker.Host.Infrastructure.Jobs;

/// <summary>Sends the digest. Scheduled by whoever asks for one, and run by the worker host's job processor.</summary>
[Job]
public sealed partial class SendDigestJob(IDigestSender sender) : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => sender.SendAsync(context.JobId, ct);
}
