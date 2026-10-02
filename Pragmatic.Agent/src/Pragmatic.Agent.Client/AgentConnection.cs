using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Client connection to the local Pragmatic Agent daemon.
///     Handles socket communication, request/response correlation, and reconnection.
/// </summary>
public sealed class AgentConnection(string socketPath) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private Stream? _stream;
    private Task? _readLoop;
    private long _messageId;
    private int _disposed;

    /// <summary>Fired when the Agent pushes a KV change notification.</summary>
    public event Action<KvChangedPayload>? OnKvChanged;

    /// <summary>Fired when the Agent sends a command (maintenance, drain, etc.).</summary>
    public event Action<CommandPayload>? OnCommand;

    /// <summary>Fired when the Agent notifies a state transition.</summary>
    public event Action<StateChangePayload>? OnStateChange;

    /// <summary>
    ///     Fired each time this client registers with the Agent — after the first connection and after every
    ///     reconnect. From here the Agent answers its reads (it refuses an unregistered client), and what a
    ///     reader holds from before may be stale: the Agent may have been written while nobody listened.
    /// </summary>
    public event Action? OnRegistered;

    /// <summary>Whether the connection to the Agent is active.</summary>
    public bool IsConnected => _stream is not null;

    /// <summary>Connects to the local Agent and starts the read loop.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var stream = await CreateStreamAsync(ct).ConfigureAwait(false);
        _stream = stream;
        _readLoop = ReadLoopAsync(stream, _cts.Token);
    }

    /// <summary>Registers this app instance with the Agent.</summary>
    /// <param name="appId">The app this instance belongs to.</param>
    /// <param name="appName">The app's display name.</param>
    /// <param name="version">The app's version, if known.</param>
    /// <param name="hostType">The host role name (mirrors <c>HostType</c>); null → the Agent's default.</param>
    /// <param name="startedAt">When the instance started (UTC); null → the Agent stamps the registration.</param>
    /// <param name="instanceId">
    ///     This running instance — a host passes its <c>IHostIdentity.HostId</c>; null lets the Agent name
    ///     the connection. Each instance is its own roster entry.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the Agent accepted the registration.</returns>
    public async Task<bool> RegisterAsync(
        string appId,
        string appName,
        string? version = null,
        string? hostType = null,
        DateTimeOffset? startedAt = null,
        string? instanceId = null,
        CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.Register, new RegisterPayload
        {
            AppId = appId,
            AppName = appName,
            InstanceId = instanceId,
            Version = version,
            ProcessId = Environment.ProcessId,
            HostType = hostType,
            StartedAt = startedAt
        }, ct).ConfigureAwait(false);

        var registered = response?.Success == true;
        if (registered)
            OnRegistered?.Invoke();
        return registered;
    }

    /// <summary>Gets a KV entry by key.</summary>
    public async Task<(string? Value, long Version, bool Found)> KvGetAsync(string key, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.KvGet, new KvGetPayload { Key = key }, ct).ConfigureAwait(false);
        ThrowIfRefused(MessageType.KvGet, response);
        if (response?.Payload is null)
            return (null, 0, false);

        var result = response.Payload.Value.Deserialize<KvEntryPayload>();
        return result is null ? (null, 0, false) : (result.Value, result.Version, result.Found);
    }

    /// <summary>Sets a KV entry. Returns (version, casConflict).</summary>
    public async Task<(long Version, bool CasConflict)> KvSetAsync(string key, string value, long? expectedVersion = null, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.KvSet, new KvSetPayload
        {
            Key = key,
            Value = value,
            ExpectedVersion = expectedVersion
        }, ct).ConfigureAwait(false);

        // A compare-and-swap conflict answers Success = false with a payload and no error: an answer,
        // not a refusal, and it goes on to be read below.
        ThrowIfRefused(MessageType.KvSet, response);
        if (response?.Payload is null)
            return (-1, false);

        var result = response.Payload.Value.Deserialize<KvSetResultPayload>();
        return result is null ? (-1, false) : (result.Version, result.CasConflict);
    }

    /// <summary>
    ///     Sets a KV entry that lives as long as this connection: the Agent deletes it when this client
    ///     disconnects — a stop, a crash, or a dispose — and the delete reaches every Agent in the cluster.
    /// </summary>
    /// <returns>Whether the Agent accepted the write.</returns>
    public async Task<bool> KvSetEphemeralAsync(string key, string value, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.KvSet, new KvSetPayload
        {
            Key = key,
            Value = value,
            Ephemeral = true
        }, ct).ConfigureAwait(false);

        return response?.Success == true;
    }

    /// <summary>Deletes a KV entry.</summary>
    public async Task<bool> KvDeleteAsync(string key, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.KvDelete, new KvDeletePayload { Key = key }, ct).ConfigureAwait(false);
        return response?.Success == true;
    }

    /// <summary>Gets all entries matching a prefix.</summary>
    public async Task<IReadOnlyList<KvEntryPayload>> KvPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(MessageType.KvPrefix, new KvPrefixPayload { Prefix = prefix }, ct).ConfigureAwait(false);
        ThrowIfRefused(MessageType.KvPrefix, response);
        if (response?.Payload is null)
            return [];

        var result = response.Payload.Value.Deserialize<KvEntriesPayload>();
        return result?.Entries ?? [];
    }

    /// <summary>Sends a heartbeat to the Agent, optionally reporting the current lifecycle state.</summary>
    public async Task HeartbeatAsync(string appId, string health = "healthy", string? state = null, CancellationToken ct = default)
    {
        await SendRequestAsync(MessageType.Heartbeat, new HeartbeatPayload
        {
            AppId = appId,
            Health = health,
            State = state
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent per the IAsyncDisposable contract: a second call must be a safe no-op rather than
        // throwing ObjectDisposedException from _cts.Cancel() on the already-disposed source.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _cts.Cancel();

        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();

        // Atomically take the stream so the read loop's finally (which also clears _stream) cannot
        // race into a double dispose or a null-deref between the check and the dispose.
        var stream = Interlocked.Exchange(ref _stream, null);
        if (stream is not null)
            await stream.DisposeAsync().ConfigureAwait(false);

        _cts.Dispose();
    }

    /// <summary>
    ///     A response carrying the daemon's error is a refusal, and a refusal is not data: read as
    ///     a payload, it deserialized to nothing and a caller got an empty roster or a missing key.
    /// </summary>
    /// <remarks>
    ///     Only for the calls that answer with data. The ones that answer a <see cref="bool" /> —
    ///     registering, deleting, an ephemeral write — already report a refusal as <c>false</c>. A null
    ///     response is no connection, which callers decide on <see cref="IsConnected" />.
    /// </remarks>
    private static void ThrowIfRefused(MessageType request, AgentMessage? response)
    {
        if (response is { Success: false, Error: { Length: > 0 } reason })
            throw new AgentRequestRefusedException(request, reason);
    }

    // Serialises concurrent writes so that concurrent callers do not interleave frames.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private async Task<AgentMessage?> SendRequestAsync<T>(MessageType type, T payload, CancellationToken ct)
    {
        // Capture _stream under a read that is only safe because _sendLock prevents
        // concurrent writers and DisposeAsync nulls _stream only after cancelling _cts.
        var stream = _stream;
        if (stream is null)
            return null;

        var id = Interlocked.Increment(ref _messageId).ToString();
        var tcs = new TaskCompletionSource<AgentMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var message = new AgentMessage
            {
                Type = type,
                Id = id,
                Payload = JsonSerializer.SerializeToElement(payload)
            };

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check after acquiring lock: DisposeAsync may have nulled _stream.
                if (_stream is null)
                    return null;

                await FrameCodec.WriteFrameAsync(_stream, message, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var reg = linked.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await FrameCodec.ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (message is null)
                    break;

                switch (message.Type)
                {
                    case MessageType.Response:
                        if (message.Id is not null && _pending.TryRemove(message.Id, out var tcs))
                            tcs.TrySetResult(message);
                        break;

                    case MessageType.KvChanged:
                        if (message.Payload is not null)
                        {
                            var kvChanged = message.Payload.Value.Deserialize<KvChangedPayload>();
                            if (kvChanged is not null) OnKvChanged?.Invoke(kvChanged);
                        }
                        break;

                    case MessageType.Command:
                        if (message.Payload is not null)
                        {
                            var cmd = message.Payload.Value.Deserialize<CommandPayload>();
                            if (cmd is not null) OnCommand?.Invoke(cmd);
                        }
                        break;

                    case MessageType.StateChange:
                        if (message.Payload is not null)
                        {
                            var state = message.Payload.Value.Deserialize<StateChangePayload>();
                            if (state is not null) OnStateChange?.Invoke(state);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { } // Disconnected
        finally
        {
            // The connection is dead once the read loop exits (EOF / IOException / cancellation).
            // Clear _stream — but only if it is still THIS loop's stream, since a concurrent reconnect
            // may already have installed a new one. This flips IsConnected to false, which is what lets
            // the heartbeat service reconnect and the L0 fallback stores activate; without it a single
            // disconnect wedged IsConnected==true forever.
            if (Interlocked.CompareExchange(ref _stream, null, stream) == stream)
            {
                try { stream.Dispose(); }
                catch { /* best effort — the connection is already broken */ }
            }
        }
    }

    private async Task<Stream> CreateStreamAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeName = Path.GetFileName(socketPath);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ct).ConfigureAwait(false);
            return pipe;
        }

        var socket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }
}
