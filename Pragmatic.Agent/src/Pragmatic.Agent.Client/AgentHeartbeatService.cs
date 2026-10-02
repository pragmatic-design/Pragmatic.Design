using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Background service that sends periodic heartbeats to the Agent
///     and handles reconnection if the Agent becomes unavailable.
/// </summary>
internal sealed class AgentHeartbeatService(
    AgentConnection connection,
    AgentOptions options,
    ILoggerFactory? loggerFactory = null,
    IHostIdentity? identity = null,
    IHostStatus? status = null,
    AgentInstanceAnnouncer? announcer = null)
    : BackgroundService
{
    private readonly ILogger _logger = loggerFactory?.CreateLogger<AgentHeartbeatService>() ?? NullLogger<AgentHeartbeatService>.Instance;

    // The one UseAgent shares with the control plane, or this service's own when it is built by hand.
    private readonly AgentInstanceAnnouncer _instance = announcer ?? new AgentInstanceAnnouncer(connection, options, identity, loggerFactory);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial connection attempt
        await TryConnectAsync(stoppingToken).ConfigureAwait(false);

        // Heartbeat loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.HeartbeatInterval, stoppingToken).ConfigureAwait(false);

                if (!connection.IsConnected && options.AutoReconnect)
                {
                    await TryConnectAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (connection.IsConnected)
                {
                    await connection.HeartbeatAsync(
                        _instance.AppId,
                        "healthy",
                        // Report the host lifecycle state so the roster descriptor (state/app:) reflects
                        // maintenance/draining/etc. — the single source the Gateway and others read.
                        state: status?.State.ToString(),
                        ct: stoppingToken).ConfigureAwait(false);

                    // A change of state that nobody reported (maintenance mode switched on locally) still
                    // moves the instance in or out of the rotation, a heartbeat later.
                    if (status is not null)
                        await _instance.RefreshAsync(status.State, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent heartbeat failed — will retry");
            }
        }
    }

    private async Task TryConnectAsync(CancellationToken ct)
    {
        try
        {
            await connection.ConnectAsync(ct).ConfigureAwait(false);

            var registered = await connection.RegisterAsync(
                _instance.AppId,
                _instance.AppName,
                System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                // Real host role + start time from the host identity, so the roster (GetAllHostsAsync)
                // reflects the actual topology instead of a hardcoded Tenant/now.
                hostType: identity?.HostType.ToString(),
                startedAt: identity?.StartedAt,
                instanceId: _instance.InstanceId,
                ct: ct).ConfigureAwait(false);

            if (registered)
            {
                _logger.LogInformation("Connected to Pragmatic Agent at {SocketPath}", options.SocketPath);
                // In the rotation unless the host is draining, drained or in maintenance — a reconnect of a
                // drained instance must not put it back.
                await _instance.AnnounceAsync(AgentInstanceAnnouncer.InRotation(status?.State ?? HostState.Ready), ct)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Agent rejected registration");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Agent not available at {SocketPath} — running in L0 mode", options.SocketPath);
        }
    }
}
