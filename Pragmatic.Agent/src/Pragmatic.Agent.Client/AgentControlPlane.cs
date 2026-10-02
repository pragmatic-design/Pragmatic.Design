using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.ControlPlane;

namespace Pragmatic.Agent.Client;

/// <summary>
///     <see cref="IControlPlane"/> implementation backed by the local Pragmatic Agent socket.
///     Replaces <c>SignalRControlPlane</c> as the coordination backbone.
///     Falls back gracefully when the Agent is unreachable.
/// </summary>
public sealed class AgentControlPlane : IControlPlane, IDisposable
{
    private readonly AgentConnection _connection;
    private readonly IServiceProvider? _services;

    // At-least-once delivery means the same command id can arrive more than once (redelivery on
    // reconnect, or the stale-sweep race). A bounded seen-set makes the effect exactly-once: a
    // second delivery of an id already handled is dropped before it reaches the dispatcher.
    private const int SeenCapacity = 8192;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly Lock _seenLock = new();

    /// <param name="connection">The socket connection to the local Agent daemon.</param>
    /// <param name="services">
    ///     Resolves <see cref="IHostCommandDispatcher"/> to execute delivered commands. Null in
    ///     transport-only scenarios (tests, monolith without command handlers) — commands are then
    ///     received and de-duplicated but not dispatched.
    /// </param>
    public AgentControlPlane(AgentConnection connection, IServiceProvider? services = null)
    {
        _connection = connection;
        _services = services;
        _connection.OnCommand += HandleIncomingCommand;
    }

    /// <inheritdoc />
    public bool IsConnected => _connection.IsConnected;

    /// <inheritdoc />
    /// <remarks>
    ///     The host's state reaches the Agent now, not a heartbeat later: a heartbeat carrying it (the roster
    ///     entry), and the instance's announcement in or out of the rotation. A drain calls this as it starts,
    ///     and this is what takes the instance out of the rotation. Leaving it to the heartbeat is not
    ///     enough: the heartbeat reports the state up to an interval late, and never the rotation.
    /// </remarks>
    public async Task ReportStatusAsync(CancellationToken ct = default)
    {
        if (!_connection.IsConnected
            || _services?.GetService<IHostStatus>() is not { } status
            || _services.GetService<AgentInstanceAnnouncer>() is not { } instance)
        {
            return;
        }

        var state = status.State;
        await _connection.HeartbeatAsync(instance.AppId, "healthy", state.ToString(), ct).ConfigureAwait(false);
        await instance.AnnounceAsync(AgentInstanceAnnouncer.InRotation(state), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostInfo>> GetAllHostsAsync(CancellationToken ct = default)
    {
        if (!_connection.IsConnected)
            return [];

        // Read registered hosts from Agent KV. Each value is a HostDescriptorPayload written by the
        // daemon on Register and refreshed on Heartbeat (gossip-replicated across the cluster).
        var entries = await _connection.KvPrefixAsync(HostRosterKeys.Prefix, ct).ConfigureAwait(false);

        var hosts = new List<HostInfo>();
        foreach (var entry in entries)
        {
            var descriptor = TryDeserializeDescriptor(entry.Value);

            hosts.Add(descriptor is null
                // Tolerate a garbled value: still surface the host, mark it Starting.
                ? new HostInfo
                {
                    HostId = InstanceOf(entry.Key),
                    HostName = entry.Key[HostRosterKeys.Prefix.Length..],
                    HostType = HostType.Tenant,
                    State = HostState.Starting,
                    LastHeartbeat = DateTimeOffset.UtcNow,
                    StartedAt = DateTimeOffset.UtcNow
                }
                // One entry per running instance: HostId is the instance, HostName the app.
                : new HostInfo
                {
                    HostId = descriptor.InstanceId,
                    HostName = descriptor.AppName,
                    HostType = ParseHostType(descriptor.HostType),
                    State = ParseHostState(descriptor.State),
                    LastHeartbeat = descriptor.LastHeartbeat,
                    StartedAt = descriptor.StartedAt
                });
        }

        return hosts;
    }

    /// <summary>
    ///     The instance a roster key lists: its last segment (<see cref="HostRosterKeys.Key" />). Read off the
    ///     key rather than the value because a deletion carries no value.
    /// </summary>
    private static string InstanceOf(string rosterKey) => rosterKey[(rosterKey.LastIndexOf('/') + 1)..];

    /// <summary>KV key prefix that <see cref="BroadcastEventAsync"/> publishes to and the stream reads back.</summary>
    private const string EventKeyPrefix = "events/";

    private static HostDescriptorPayload? TryDeserializeDescriptor(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<HostDescriptorPayload>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Reads back an event published by <see cref="BroadcastEventAsync"/>. Tolerates a garbled or
    ///     unknown-discriminator value by dropping it: one bad writer must not tear down every
    ///     subscriber's stream.
    /// </summary>
    private static ControlPlaneEvent? TryDeserializeEvent(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ControlPlaneEvent>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HostType ParseHostType(string? value)
        => Enum.TryParse<HostType>(value, ignoreCase: true, out var t) ? t : HostType.Tenant;

    /// <inheritdoc />
    public async IAsyncEnumerable<ControlPlaneEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // The Agent pushes KV changes over the socket via OnKvChanged. We translate the relevant
        // key prefixes into typed control-plane events: host lifecycle (state/app:), config, and the
        // events/ keys BroadcastEventAsync publishes to.
        var channel = Channel.CreateBounded<KvChangedPayload>(128);

        _connection.OnKvChanged += OnChange;
        // Complete the writer on cancellation so ReadAllAsync drains and exits cleanly, and any
        // in-flight OnChange TryWrite becomes a harmless no-op instead of dangling.
        using var ctReg = ct.Register(static state => ((Channel<KvChangedPayload>)state!).Writer.TryComplete(), channel);

        try
        {
            // A KvChanged frame carries only the new value, so the state a host came *from* has to be
            // remembered. Seed it from the current roster, per subscriber, so the first transition an
            // observer sees already reports a real OldState instead of a constant.
            var lastStates = new Dictionary<string, HostState>(StringComparer.Ordinal);
            foreach (var host in await GetAllHostsAsync(ct).ConfigureAwait(false))
                lastStates[host.HostId] = host.State;

            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var evt = MapToEvent(change, lastStates);
                if (evt is not null)
                    yield return evt;
            }
        }
        finally
        {
            _connection.OnKvChanged -= OnChange;
            channel.Writer.TryComplete();
        }

        void OnChange(KvChangedPayload payload)
        {
            if (payload.Key.StartsWith(HostRosterKeys.Prefix, StringComparison.Ordinal)
                || payload.Key.StartsWith("config/", StringComparison.Ordinal)
                || payload.Key.StartsWith(EventKeyPrefix, StringComparison.Ordinal))
                channel.Writer.TryWrite(payload);
        }
    }

    private static ControlPlaneEvent? MapToEvent(
        KvChangedPayload change, Dictionary<string, HostState> lastStates)
    {
        if (change.Key.StartsWith(HostRosterKeys.Prefix, StringComparison.Ordinal))
        {
            // Per instance, as GetAllHostsAsync seeds it: two instances of one app move independently.
            var hostId = InstanceOf(change.Key);

            // The value under state/app: is a whole HostDescriptorPayload document, not a bare state
            // name — GetAllHostsAsync above deserializes it correctly. Handing the document to
            // ParseHostState made every transition fall through to the Starting fallback.
            var newState = change.Deleted
                ? HostState.Stopped
                : ParseHostState(TryDeserializeDescriptor(change.Value)?.State);

            var oldState = lastStates.TryGetValue(hostId, out var previous) ? previous : HostState.Starting;

            if (change.Deleted)
                lastStates.Remove(hostId);
            else
                lastStates[hostId] = newState;

            // Every heartbeat rewrites the descriptor (lastHeartbeat moves), so most changes under this
            // prefix are not transitions at all. Now that OldState is real, emitting them would be
            // publishing a "changed" event that says nothing changed.
            if (oldState == newState)
                return null;

            return new HostStateChangedEvent(
                SourceHostId: hostId,
                Timestamp: DateTimeOffset.UtcNow,
                OldState: oldState,
                NewState: newState,
                Reason: change.Deleted ? "deregistered" : null);
        }

        if (change.Key.StartsWith(EventKeyPrefix, StringComparison.Ordinal))
        {
            // The other half of BroadcastEventAsync. Without this branch the events it published were
            // written to the cluster and read by nobody.
            return change.Deleted ? null : TryDeserializeEvent(change.Value);
        }

        if (change.Key.StartsWith("config/", StringComparison.Ordinal))
        {
            var key = change.Key["config/".Length..];
            return new ConfigChangedEvent(
                SourceHostId: string.Empty,
                Timestamp: DateTimeOffset.UtcNow,
                Key: key,
                OldValue: null,
                NewValue: change.Deleted ? null : change.Value,
                TenantId: null);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ControlPlaneError?> SendCommandAsync(
        string targetHostId, HostCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_connection.IsConnected)
            return ControlPlaneError.NotConnected();

        // Losslessly serialize the whole command: JsonSerializer.Serialize<HostCommand> emits the
        // [JsonPolymorphic] $type discriminator plus every field (CommandId, CorrelationId, Drain
        // grace, migrate target, …). The previous anonymous {type,target,timestamp} shape dropped the
        // payload, so Drain/Migrate/maintenance commands arrived with no fields.
        var envelope = new CommandPayload
        {
            CommandType = command.CommandTypeName,
            CommandId = command.CommandId,
            TargetHostId = targetHostId,
            Data = JsonSerializer.SerializeToElement<HostCommand>(command),
        };

        // The target is an instance — the HostId GetAllHostsAsync lists — and the Agent delivers
        // to that connection only. Key by the stable command id so a re-send is idempotent and the ACK path
        // can map commands-ack/{targetHostId}/{commandId} straight back to this key for cleanup.
        var commandKey = $"commands/{targetHostId}/{command.CommandId}";
        var (_, casConflict) = await _connection
            .KvSetAsync(commandKey, JsonSerializer.Serialize(envelope), ct: ct).ConfigureAwait(false);
        return casConflict ? ControlPlaneError.CommandFailed("Command write conflict") : null;
    }

    /// <summary>
    ///     Receives a command frame pushed by the daemon, de-duplicates by command id, dispatches it to
    ///     the registered <see cref="IHostCommandDispatcher"/>, then ACKs so the daemon can clean up the
    ///     KV key.
    /// </summary>
    /// <remarks>
    ///     Delivery is <b>at-least-once</b>. The de-dup set (<see cref="MarkSeen"/>) is per-process and
    ///     in-memory, so it collapses a gossip re-delivery within a single app lifetime — but a redelivery
    ///     <i>after the consuming app restarts</i> (a crash between dispatch and ACK) re-executes. Host
    ///     commands are therefore required to be idempotent by contract (Drain / Migrate / maintenance
    ///     toggles all are); the de-dup is a best-effort optimization, not an exactly-once guarantee.
    /// </remarks>
    private void HandleIncomingCommand(CommandPayload payload)
    {
        if (payload.CommandId is { } id && !MarkSeen(id))
            return; // Already handled — drop the redelivery.

        _ = DispatchAndAckAsync(payload);
    }

    private async Task DispatchAndAckAsync(CommandPayload payload)
    {
        var dispatcher = _services?.GetService<IHostCommandDispatcher>();
        if (dispatcher is null)
            return; // No command infrastructure wired — received + de-duplicated, nothing to run.

        try
        {
            var commandJson = payload.Data?.GetRawText() ?? "{}";
            await dispatcher.DispatchAsync(payload.CommandType, commandJson, CancellationToken.None)
                .ConfigureAwait(false);

            // ACK so the daemon deletes the delivered command key. Best-effort: if the ACK write
            // fails the command lingers until the daemon's stale sweep reclaims it (de-dup still
            // prevents a double execution on any redelivery in between).
            if (payload is { CommandId: { } commandId, TargetHostId: { } hostId } && _connection.IsConnected)
            {
                await _connection.KvSetAsync($"commands-ack/{hostId}/{commandId}", "1")
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // DispatchAsync already logs and swallows per-command failures by contract; guard here too
            // so a fault in the fire-and-forget path can never crash the process.
        }
    }

    /// <summary>Records a command id as handled. Returns false if it was already present.</summary>
    private bool MarkSeen(string commandId)
    {
        lock (_seenLock)
        {
            if (!_seen.Add(commandId))
                return false;

            _seenOrder.Enqueue(commandId);
            if (_seenOrder.Count > SeenCapacity)
                _seen.Remove(_seenOrder.Dequeue());

            return true;
        }
    }

    /// <inheritdoc />
    public async Task BroadcastEventAsync(ControlPlaneEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!_connection.IsConnected)
            return;

        // Publish to the Agent KV under an events/ key; the Agent's gossip layer propagates the
        // write to every connected host, where StreamEventsAsync observers pick it up via
        // OnKvChanged. A GUID key keeps concurrent broadcasts from overwriting one another.
        var eventKey = $"{EventKeyPrefix}{evt.SourceHostId}/{Guid.NewGuid():N}";

        // Serialize through the base type, exactly as SendCommandAsync does for HostCommand: that emits
        // the [JsonPolymorphic] $type discriminator plus every field of the concrete event. The previous
        // anonymous {type,sourceHostId,timestamp} shape dropped OldState/NewState/Reason (and the whole
        // of ConfigChangedEvent) and could not be read back into a typed event at all.
        var eventJson = JsonSerializer.Serialize(evt);

        await _connection.KvSetAsync(eventKey, eventJson, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _connection.OnCommand -= HandleIncomingCommand;

    private static HostState ParseHostState(string? value) => value switch
    {
        // Legacy lowercase state strings from the pre-descriptor wire format.
        "ready" => HostState.Ready,
        "maintenance" => HostState.Maintenance,
        "draining" => HostState.Draining,
        "stopped" => HostState.Stopped,
        "migrating" => HostState.Migrating,
        // Descriptor stores the enum name ("Ready", "Starting", …).
        _ => Enum.TryParse<HostState>(value, ignoreCase: true, out var s) ? s : HostState.Starting
    };
}
