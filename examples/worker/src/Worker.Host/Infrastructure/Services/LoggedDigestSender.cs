using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Attributes;

namespace Worker.Host.Infrastructure.Services;

/// <summary>Writes the digest to the log.</summary>
[Service<IDigestSender>(Lifetime = Lifetime.Singleton)]
public sealed partial class LoggedDigestSender(ILogger<LoggedDigestSender> logger) : IDigestSender
{
    public Task SendAsync(Guid jobId, CancellationToken ct)
    {
        LogDigestSent(jobId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Digest sent by job {JobId}")]
    private partial void LogDigestSent(Guid jobId);
}
