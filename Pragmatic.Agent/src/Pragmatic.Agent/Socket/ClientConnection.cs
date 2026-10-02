using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Represents a single connected client (app, CLI, gateway).
///     Reads frames, dispatches to handler, sends responses.
/// </summary>
internal sealed class ClientConnection(
    Stream stream,
    IMessageHandler handler,
    Action<ClientConnection> onDisconnected,
    TimeSpan? frameReadTimeout = null)
    : IDisposable
{
    /// <summary>
    ///     Per-frame read deadline. Once a length header is read, <see cref="FrameCodec.ReadFrameAsync"/>
    ///     reads the (up to 16 MB) payload without observing cancellation to preserve frame-boundary
    ///     integrity. A client that sends a header then stalls would otherwise pin this read-loop task
    ///     and connection indefinitely (DoS). This deadline is generous for well-behaved peers; on
    ///     expiry we dispose the stream to unblock the non-cancellable read and tear the connection down.
    /// </summary>
    private static readonly TimeSpan DefaultFrameReadTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _frameReadTimeout = frameReadTimeout ?? DefaultFrameReadTimeout;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _readLoop;

    /// <summary>The app ID, set after REGISTER message is received.</summary>
    public string? AppId { get; internal set; }

    /// <summary>
    ///     The roster key this client registered under — <c>state/app:{appId}/{instanceId}</c> — set with
    ///     <see cref="AppId" />. Its own entry, refreshed and retired by this connection and no other.
    /// </summary>
    public string? RosterKey { get; internal set; }

    /// <summary>The instance this client registered as — where a command addressed to it is delivered.</summary>
    public string? InstanceId { get; internal set; }

    private readonly Lock _ephemeralLock = new();
    private readonly HashSet<string> _ephemeralKeys = [];

    /// <summary>Records a key this client wrote as ephemeral, to be deleted when it disconnects.</summary>
    public void AddEphemeralKey(string key)
    {
        lock (_ephemeralLock)
            _ephemeralKeys.Add(key);
    }

    /// <summary>The keys this client wrote as ephemeral.</summary>
    public IReadOnlyList<string> EphemeralKeys
    {
        get
        {
            lock (_ephemeralLock)
                return [.. _ephemeralKeys];
        }
    }

    public void Start()
    {
        _readLoop = ReadLoopAsync();
        // Observe any unhandled fault from the loop so it does not become a silent fire-and-forget.
        _ = _readLoop.ContinueWith(
            t => AgentLogger.Error("Socket", $"Read loop faulted: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Sends a message to this client (thread-safe).</summary>
    public async Task SendAsync(AgentMessage message, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteFrameAsync(stream, message, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        // Disposing the stream causes ReadFrameAsync to throw IOException/ObjectDisposedException,
        // which breaks the read loop cleanly (handled in ReadLoopAsync).
        stream.Dispose();
        _writeLock.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var message = await ReadFrameWithDeadlineAsync().ConfigureAwait(false);
                if (message is null)
                    break; // Stream closed

                var response = await handler.HandleAsync(this, message).ConfigureAwait(false);
                if (response is not null)
                    await SendAsync(response).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Normal disconnection (also the path a deadline-triggered stream Dispose takes).
        }
        catch (Exception ex)
        {
            AgentLogger.Error("Socket", $"Client error: {ex.Message}");
        }
        finally
        {
            onDisconnected(this);
        }
    }

    /// <summary>
    ///     Reads one frame under a per-frame deadline. FrameCodec deliberately ignores cancellation
    ///     once a payload is in flight, so a stalled payload read cannot be cancelled cooperatively:
    ///     on timeout we dispose the stream, which makes the pending read throw and unblocks the loop.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The deadline is armed at the frame's <b>first byte</b>, not before it. Waiting for the next
    ///     message is not a stall: a client that has nothing to say — the gateway, which does not
    ///     heartbeat — was disconnected thirty seconds after its last frame while the deadline started
    ///     before the header, and lost its registration and subscriptions with the connection.
    ///     A peer that goes away while idle is still seen: its closed stream ends the read.
    /// </remarks>
    private async Task<AgentMessage?> ReadFrameWithDeadlineAsync()
    {
        using var timeoutCts = new CancellationTokenSource();
        await using var registration = timeoutCts.Token.Register(static state =>
        {
            // Force-abort a stalled (non-cancellable) payload read by tearing down the stream.
            var s = (Stream)state!;
            try { s.Dispose(); }
            catch { /* best effort — the read loop observes the resulting IOException/ObjectDisposed */ }
        }, stream).ConfigureAwait(false);

        try
        {
            return await FrameCodec
                .ReadFrameAsync(stream, timeoutCts.Token, onFrameStarted: () => timeoutCts.CancelAfter(_frameReadTimeout))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested
                                   && ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            AgentLogger.Error("Socket", $"Frame read timed out after {_frameReadTimeout.TotalSeconds:0}s; closing connection.");
            return null; // Treat a stalled peer as a disconnect — loop exits cleanly.
        }
    }
}
