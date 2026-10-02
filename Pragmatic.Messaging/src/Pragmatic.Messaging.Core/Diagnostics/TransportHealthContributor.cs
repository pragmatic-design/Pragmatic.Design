using Pragmatic.ControlPlane;

namespace Pragmatic.Messaging.Diagnostics;

/// <summary>
///     Reports a message transport's connection state to the host's composite health report.
/// </summary>
/// <remarks>
///     <para>
///         One class for every transport, because <see cref="IMessageTransport" /> already exposes the
///         only two things a health report needs: <see cref="IMessageTransport.Name" /> and
///         <see cref="IMessageTransport.Status" />. A class per transport would run the same three-way
///         switch on <c>transport.Status</c>, differing only in which return type it built.
///     </para>
///     <para>
///         Registration happens next to the transport it describes, in each <c>Use…</c> extension. A
///         contributor nobody registers leaves <c>HostHealthAggregator</c> — which the generated host
///         does register — collecting an empty set, and the messaging half of host health reporting
///         nothing at all.
///     </para>
///     <para>
///         There is no per-transport <c>IHealthCheck</c>: <c>ControlPlaneHealthCheck</c> already bridges
///         <see cref="IHostHealthAggregator" /> to the ASP.NET health endpoint, so a contributor reaches
///         <c>/health</c> through it. A per-transport IHealthCheck would duplicate this logic to reach a
///         channel that is already covered.
///     </para>
/// </remarks>
public sealed class TransportHealthContributor(IMessageTransport transport) : IHostHealthContributor
{
    /// <inheritdoc />
    public string Name { get; } = $"Messaging.{transport.Name}";

    /// <inheritdoc />
    public string? Category => "Messaging";

    /// <inheritdoc />
    public HealthContributorMode Mode => HealthContributorMode.Push;

    /// <inheritdoc />
    public Task<ContributorHealthReport> CheckAsync(CancellationToken ct = default)
    {
        var report = transport.Status switch
        {
            TransportStatus.Connected => ContributorHealthReport.Healthy($"{transport.Name} connected"),
            TransportStatus.Connecting => ContributorHealthReport.Degraded($"{transport.Name} connecting"),
            _ => ContributorHealthReport.Unhealthy($"{transport.Name}: {transport.Status}")
        };

        return Task.FromResult(report);
    }
}
