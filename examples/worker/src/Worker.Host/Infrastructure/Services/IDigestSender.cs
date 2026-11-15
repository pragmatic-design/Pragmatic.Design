namespace Worker.Host.Infrastructure.Services;

/// <summary>Where a digest goes. The worker logs it; a deployment would mail it.</summary>
public interface IDigestSender
{
    Task SendAsync(Guid jobId, CancellationToken ct);
}
