using Microsoft.Extensions.Hosting;
using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Starts listening for the commands the Agent delivers to this host when the host starts: it holds the
///     host's <see cref="IControlPlane" />, which <see cref="AgentControlPlane" /> subscribes to
///     <see cref="AgentConnection.OnCommand" /> as it is built.
/// </summary>
/// <remarks>
///     ⚠️ Without it nothing built the control plane until a command handler asked for it — and the handlers are
///     resolved only after a command has been received. Every command the Agent delivered to a host started
///     with <c>UseAgent()</c> reached a connection nobody listened on. A hosted service, because
///     the host starts it, and the injection is what builds the control plane — explicitly, beside the
///     registration that makes it the Agent's.
/// </remarks>
internal sealed class AgentCommandListener(IControlPlane controlPlane) : IHostedService
{
    /// <summary>The control plane now listening; kept so the listening has an owner.</summary>
    public IControlPlane ControlPlane { get; } = controlPlane;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
