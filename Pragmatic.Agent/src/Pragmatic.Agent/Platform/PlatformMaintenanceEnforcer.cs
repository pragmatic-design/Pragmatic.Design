using System.Text.Json;
using System.Threading.Channels;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;

namespace Pragmatic.Agent.Platform;

/// <summary>
///     Enforces per-app maintenance on the local platform. When an app hosted by THIS daemon transitions
///     into (or out of) <c>Maintenance</c> — observed via its <c>state/app:{appId}/{instanceId}</c> descriptor, which
///     the heartbeat folds the reported lifecycle state into — the detected <see cref="IAgentPlatformAdapter"/>
///     is invoked to activate/deactivate the platform-native maintenance mechanism (e.g. an nginx/IIS
///     maintenance config, or a no-op where the Gateway already serves 503 from the KV signal).
/// </summary>
/// <remarks>
///     Only apps connected to this daemon are enforced: the adapter acts on the local platform (a
///     <c>docker stop</c> / local reverse-proxy rewrite), so a gossiped descriptor for an app hosted on
///     another node is ignored here — that node's own daemon enforces it.
/// </remarks>
internal sealed class PlatformMaintenanceEnforcer : IDisposable
{
    private const string Prefix = HostRosterKeys.Prefix;
    private const string MaintenanceState = "Maintenance";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AgentSocketServer _server;
    private readonly IAgentPlatformAdapter _adapter;
    private readonly Channel<KvChangeEvent> _channel;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _cts = new();

    // Last-known maintenance flag per roster entry, so only real transitions drive the adapter (a heartbeat
    // rewrites the descriptor every interval; without this every heartbeat would re-fire the adapter).
    private readonly Dictionary<string, bool> _inMaintenance = new(StringComparer.Ordinal);

    // The app each roster entry belongs to, from its descriptor: a deletion carries no value to read it from,
    // and the key is per instance, not the app.
    private readonly Dictionary<string, string> _appOfEntry = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();

    public PlatformMaintenanceEnforcer(KvStore kvStore, AgentSocketServer server, IAgentPlatformAdapter adapter)
    {
        _server = server;
        _adapter = adapter;
        _channel = Channel.CreateBounded<KvChangeEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscription = kvStore.Watch(Prefix, _channel);
        _ = PumpAsync(_cts.Token);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var descriptor = evt.Deleted ? null : ReadDescriptor(evt.Value);
                string? appId;
                lock (_stateLock)
                {
                    if (descriptor is not null)
                        _appOfEntry[evt.Key] = descriptor.AppId;
                    _appOfEntry.TryGetValue(evt.Key, out appId);
                    if (evt.Deleted)
                        _appOfEntry.Remove(evt.Key);
                }

                // Only enforce for apps this daemon hosts — the adapter operates on the local platform.
                if (appId is null || !_server.IsAppConnected(appId))
                    continue;

                var nowInMaintenance = string.Equals(descriptor?.State, MaintenanceState, StringComparison.OrdinalIgnoreCase);

                bool wasInMaintenance;
                lock (_stateLock)
                {
                    _inMaintenance.TryGetValue(evt.Key, out wasInMaintenance);
                    if (evt.Deleted)
                        _inMaintenance.Remove(evt.Key);
                    else
                        _inMaintenance[evt.Key] = nowInMaintenance;
                }

                if (nowInMaintenance == wasInMaintenance)
                    continue; // Not a transition — ignore heartbeat rewrites.

                try
                {
                    if (nowInMaintenance)
                        await _adapter.ActivateMaintenanceAsync(appId, ct).ConfigureAwait(false);
                    else
                        await _adapter.DeactivateMaintenanceAsync(appId, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // Shutting down.
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("Platform",
                        $"{_adapter.PlatformName} maintenance enforcement for {appId} failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
    }

    private static HostDescriptorPayload? ReadDescriptor(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<HostDescriptorPayload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _subscription.Dispose();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }
}
