using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Pragmatic.Agent.KV;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     SWIM (Scalable Weakly-consistent Infection-style Membership) protocol implementation.
///     Handles: periodic ping, indirect probe, suspect/dead lifecycle, KV piggyback replication.
/// </summary>
internal sealed partial class SwimProtocol : IDisposable
{
    private readonly string _selfId;
    private readonly int _port;
    private readonly KvStore _kvStore;
    private readonly string _bindAddress;

    // Cluster-shared HMAC. When null, gossip runs FAIL-CLOSED: outbound datagrams are still sent
    // (membership can still form on an unsecured network), but inbound membership/KV mutations from
    // remote peers are NOT applied — an unauthenticated network cannot inject routes/secrets
    // or forge deletes. A loud startup warning is emitted in this mode.
    private readonly GossipAuthenticator? _authenticator;
    private bool RemoteMutationsTrusted => _authenticator is not null;

    public SwimProtocol(
        string selfId,
        int port,
        KvStore kvStore,
        TimeSpan? suspectTimeout = null,
        GossipAuthenticator? authenticator = null,
        string bindAddress = "0.0.0.0")
    {
        _selfId = selfId;
        _port = port;
        _kvStore = kvStore;
        _authenticator = authenticator;
        _bindAddress = bindAddress;
        _membership = new MembershipTable(selfId, suspectTimeout);
        _membership.OnMemberChanged += RetireWhatADeadMemberOwned;

        if (authenticator is null)
        {
            AgentLogger.Warn("Gossip",
                "No gossip shared key configured (Gossip:SharedKey / PRAGMATIC_AGENT_GOSSIP_KEY). " +
                "Datagrams are UNAUTHENTICATED: remote KV and membership mutations will be REJECTED " +
                "(fail-closed). Set a cluster-shared key to enable replication across agents.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Max size of an inbound gossip datagram before deserialization. A UDP payload cannot exceed
    // ~65507 bytes, but we cap lower to bound allocation/parse work from a hostile or malformed
    // packet (DoS guard) — legitimate piggybacked membership/KV gossip stays well under this.
    private const int MaxGossipPacketSize = 64 * 1024;

    private readonly MembershipTable _membership;
    private readonly CancellationTokenSource _cts = new();

    // Infection-style piggyback: each KV update rides on the next few outbound datagrams instead
    // of being drained into a single one, so one lost UDP packet does not silently lose the update.
    private const int KvPiggybackTransmissions = 3;
    private readonly List<PendingKvUpdate> _pendingKvUpdates = [];
    private readonly Lock _kvLock = new();

    private sealed class PendingKvUpdate(KvUpdate update)
    {
        public KvUpdate Update { get; } = update;
        public int SendsLeft { get; set; } = KvPiggybackTransmissions;
    }

    private UdpClient? _udp;
    private Task? _listenTask;
    private Task? _probeTask;
    private Task? _evictionTask;

    // Pending ack tracking for failure detection
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingAcks = [];
    private readonly Lock _ackLock = new();

    // Metered count of datagrams dropped because they failed HMAC verification (or were unsigned
    // while a key was configured). Exposed for observability/tests.
    private long _droppedUnauthenticated;

    public MembershipTable Membership => _membership;

    /// <summary>Number of gossip datagrams dropped for failing HMAC authentication.</summary>
    public long DroppedUnauthenticatedCount => Interlocked.Read(ref _droppedUnauthenticated);

    // Cap concurrent off-loop message handlers. In keyless mode auth cannot pre-filter datagrams, so
    // an unauthenticated UDP flood would otherwise spawn unbounded Task.Run handlers (each a PingReq
    // may wait ~400ms) → task/thread exhaustion. Once the cap is hit, excess datagrams are dropped.
    private readonly SemaphoreSlim _handlerLimit = new(64, 64);
    private long _droppedOverload;

    /// <summary>Number of gossip datagrams dropped because the handler concurrency cap was saturated.</summary>
    public long DroppedOverloadCount => Interlocked.Read(ref _droppedOverload);

    // The KV-change subscription used for gossip replication is registered in ClusterManager
    // after calling _swim.Start(), which drains the channel in a background Task.Run pump.
    // This constructor does not create a channel of its own: nothing would consume it.

    /// <summary>Starts the SWIM protocol: UDP listener, periodic probing, eviction.</summary>
    public void Start()
    {
        // Bind to the configured interface. Default 0.0.0.0 preserves cross-host clustering; a
        // single-host or NIC-restricted deployment can narrow this via ClusterConfig.BindAddress.
        var bindIp = IPAddress.TryParse(_bindAddress, out var parsed) ? parsed : IPAddress.Any;
        _udp = new UdpClient(new IPEndPoint(bindIp, _port));
        _listenTask = ListenAsync(_cts.Token);
        _probeTask = ProbeLoopAsync(_cts.Token);
        _evictionTask = EvictionLoopAsync(_cts.Token);
        _antiEntropyTask = AntiEntropyLoopAsync(_cts.Token);
    }

    /// <summary>Joins the cluster by contacting a known peer.</summary>
    public async Task JoinAsync(string host, int port)
    {
        var joinMessage = new GossipMessage
        {
            Type = GossipMessageType.Join,
            SenderId = _selfId,
            Members = _membership.BuildMemberUpdates()
        };

        await SendToAsync(joinMessage, host, port).ConfigureAwait(false);
    }

    /// <summary>Queues a KV update for piggybacking on the next few gossip messages.</summary>
    public void EnqueueKvUpdate(string key, string? value, long version, bool deleted, string? owner)
    {
        lock (_kvLock)
        {
            _pendingKvUpdates.Add(new PendingKvUpdate(new KvUpdate
            {
                Key = key,
                Value = value,
                Version = version,
                Deleted = deleted,
                UpdatedAt = DateTimeOffset.UtcNow,
                Owner = owner,
            }));

            // Keep bounded — drop oldest if too many pending
            while (_pendingKvUpdates.Count > 100)
                _pendingKvUpdates.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
        _handlerLimit.Dispose();
        _cts.Dispose();
    }

    // =========================================================================
    // Core SWIM Protocol
    // =========================================================================

    /// <summary>Periodic probe: pick random member, ping, detect failures.</summary>
    private async Task ProbeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);

                var target = _membership.GetRandomAlive();
                if (target is null)
                    continue;

                var acked = await PingAsync(target, TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
                if (acked)
                {
                    _membership.MarkAlive(target.Id);
                    continue;
                }

                // Direct ping failed — try indirect probe via K random members
                var probers = _membership.GetRandomProbers(target.Id);

                // Register a TCS to receive PingReqAck from any of the K probers.
                var indirectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var indirectKey = $"indirect:{target.Id}:{DateTimeOffset.UtcNow.Ticks}";
                lock (_ackLock) { _pendingAcks[indirectKey] = indirectTcs; }

                try
                {
                    foreach (var prober in probers)
                    {
                        var pingReq = BuildMessage(GossipMessageType.PingReq, target.Id);
                        await SendToAsync(pingReq, prober.Host, prober.Port).ConfigureAwait(false);
                    }

                    // Wait for any PingReqAck within timeout
                    using var indirectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    indirectTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                    indirectTimeoutCts.Token.Register(() => indirectTcs.TrySetResult(false));

                    var indirectAcked = await indirectTcs.Task.ConfigureAwait(false);

                    if (!indirectAcked)
                        _membership.MarkSuspect(target.Id);
                }
                finally
                {
                    lock (_ackLock) { _pendingAcks.Remove(indirectKey); }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AgentLogger.Warn("Gossip", $"Probe error: {ex.Message}");
            }
        }
    }

    /// <summary>Sends a Ping and waits for Ack.</summary>
    private async Task<bool> PingAsync(ClusterMember target, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ackKey = $"ping:{target.Id}:{DateTimeOffset.UtcNow.Ticks}";

        lock (_ackLock) { _pendingAcks[ackKey] = tcs; }

        try
        {
            var ping = BuildMessage(GossipMessageType.Ping);
            await SendToAsync(ping, target.Host, target.Port).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            timeoutCts.Token.Register(() => tcs.TrySetResult(false));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_ackLock) { _pendingAcks.Remove(ackKey); }
        }
    }

    /// <summary>Promotes suspects to dead after timeout.</summary>
    private async Task EvictionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                _membership.EvictStaleMembers();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // =========================================================================
    // UDP Communication
    // =========================================================================

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct).ConfigureAwait(false);

                // DoS guard: drop oversized datagrams before allocating/parsing them.
                if (result.Buffer.Length > MaxGossipPacketSize)
                {
                    AgentLogger.Warn("Gossip", $"Dropped oversized packet ({result.Buffer.Length} bytes) from {result.RemoteEndPoint}.");
                    continue;
                }

                // Trust boundary: when a shared key is configured, every datagram MUST carry a valid
                // HMAC. Unsigned or tampered packets are dropped here (constant-time verify) and never
                // reach KV/membership ingestion. Without a key we run fail-closed (see ctor warning);
                // packets are still parsed for membership-only liveness but remote mutations are
                // rejected inside HandleMessageAsync.
                ReadOnlySpan<byte> payloadBytes;
                if (_authenticator is not null)
                {
                    if (!_authenticator.TryVerify(result.Buffer, out payloadBytes))
                    {
                        Interlocked.Increment(ref _droppedUnauthenticated);
                        AgentLogger.Warn("Gossip", $"Dropped unauthenticated/tampered gossip packet from {result.RemoteEndPoint}.");
                        continue;
                    }
                }
                else
                {
                    payloadBytes = result.Buffer;
                }

                var json = Encoding.UTF8.GetString(payloadBytes);
                var message = JsonSerializer.Deserialize<GossipMessage>(json, JsonOptions);
                if (message is not null)
                {
                    // Handle OFF the receive loop: a PingReq handler pings the target and waits up
                    // to 400ms — doing that inline stalls the UDP listener, delays Acks for our own
                    // pending pings and cascades into false suspicions under probe bursts.
                    var endpoint = result.RemoteEndPoint;

                    // Bound concurrent handlers: drop the datagram if the cap is saturated rather than
                    // spawning an unbounded task (DoS guard for keyless-mode floods).
                    if (!_handlerLimit.Wait(0))
                    {
                        Interlocked.Increment(ref _droppedOverload);
                        continue;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleMessageAsync(message, endpoint, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // shutdown
                        }
                        catch (Exception ex)
                        {
                            AgentLogger.Warn("Gossip", $"Message handling error: {ex.Message}");
                        }
                        finally
                        {
                            _handlerLimit.Release();
                        }
                    }, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AgentLogger.Warn("Gossip", $"Receive error: {ex.Message}");
            }
        }
    }

    private async Task HandleMessageAsync(GossipMessage message, IPEndPoint sender, CancellationToken ct = default)
    {
        // Fail-closed: with no shared key configured, packets reaching here are unauthenticated.
        // We do NOT apply membership or KV mutations from them — an attacker on the gossip network
        // must not be able to inject members, routes or secrets, nor forge deletes. Liveness replies
        // (Ack/PingReqAck below) still work so a key-less single node degrades gracefully.
        if (RemoteMutationsTrusted)
        {
            // The sender itself, at the endpoint it sent from: the socket sends from the port it gossips on.
            var senderAddress = sender.Address.IsIPv4MappedToIPv6 ? sender.Address.MapToIPv4() : sender.Address;
            _membership.AddHeardFrom(message.SenderId, senderAddress.ToString(), sender.Port);

            // Apply piggybacked membership updates
            if (message.Members is not null)
            {
                foreach (var update in message.Members)
                    _membership.ApplyUpdate(update);
            }

            // Apply piggybacked KV updates (LWW). Delete is version-guarded (DeleteIfNewer) so a
            // forged/stale delete can't wipe a newer entry.
            if (message.KvUpdates is not null)
            {
                foreach (var kv in message.KvUpdates)
                {
                    if (kv.Deleted)
                        _kvStore.DeleteIfNewer(kv.Key, kv.Version);
                    else if (kv.Value is not null)
                        _kvStore.SetIfNewer(kv.Key, kv.Value, kv.Version, kv.UpdatedAt, kv.Owner);
                }
            }
        }

        switch (message.Type)
        {
            case GossipMessageType.Ping:
                var ack = BuildMessage(GossipMessageType.Ack);
                await SendToAsync(ack, sender.Address.ToString(), sender.Port).ConfigureAwait(false);
                break;

            case GossipMessageType.Ack:
                // Resolve any pending ping ack
                lock (_ackLock)
                {
                    foreach (var kvp in _pendingAcks)
                    {
                        if (kvp.Key.StartsWith($"ping:{message.SenderId}:", StringComparison.Ordinal))
                        {
                            kvp.Value.TrySetResult(true);
                            break;
                        }
                    }
                }
                _membership.MarkAlive(message.SenderId);
                break;

            case GossipMessageType.PingReq:
                // Indirect probe: ping the target on behalf of sender, then send PingReqAck back.
                if (message.TargetId is not null)
                {
                    var members = _membership.GetAllMembers();
                    var pingReqTarget = members.FirstOrDefault(m => m.Id == message.TargetId);
                    if (pingReqTarget is not null)
                    {
                        var acked = await PingAsync(pingReqTarget, TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false);
                        if (acked)
                        {
                            // Notify original requester that indirect probe succeeded
                            var pingReqAck = BuildMessage(GossipMessageType.PingReqAck, message.TargetId);
                            await SendToAsync(pingReqAck, sender.Address.ToString(), sender.Port).ConfigureAwait(false);
                        }
                    }
                }
                break;

            case GossipMessageType.PingReqAck:
                // Indirect probe succeeded — resolve the pending indirect TCS.
                if (message.TargetId is not null)
                {
                    lock (_ackLock)
                    {
                        foreach (var kvp in _pendingAcks)
                        {
                            if (kvp.Key.StartsWith($"indirect:{message.TargetId}:", StringComparison.Ordinal))
                            {
                                kvp.Value.TrySetResult(true);
                                break;
                            }
                        }
                    }
                    _membership.MarkAlive(message.TargetId);
                }
                break;

            case GossipMessageType.Join:
                // New member — respond with the members and the whole KV state, so an Agent that joins
                // late holds what was written before it.
                var sync = new GossipMessage
                {
                    Type = GossipMessageType.Sync,
                    SenderId = _selfId,
                    Members = _membership.BuildMemberUpdates()
                };
                await SendToAsync(sync, sender.Address.ToString(), sender.Port).ConfigureAwait(false);
                if (RemoteMutationsTrusted)
                    await SendStateAsync(sender.Address.ToString(), sender.Port).ConfigureAwait(false);
                break;

            case GossipMessageType.Sync:
                // Members and KV state — already applied above, like any piggyback
                break;

            case GossipMessageType.Pull:
                // The pull half of the sender's anti-entropy round: answer with this Agent's state.
                if (RemoteMutationsTrusted)
                    await SendStateAsync(sender.Address.ToString(), sender.Port).ConfigureAwait(false);
                break;
        }
    }

    private async Task SendToAsync(GossipMessage message, string host, int port)
    {
        if (_udp is null) return;

        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        // Sign the datagram with the cluster-shared key so peers can authenticate it. With no key
        // configured we send the bare payload (fail-closed receivers will ignore mutations).
        var datagram = _authenticator is not null ? _authenticator.Sign(json) : json;

        await _udp.SendAsync(datagram, new IPEndPoint(IPAddress.Parse(host), port)).ConfigureAwait(false);
    }

    private GossipMessage BuildMessage(GossipMessageType type, string? targetId = null)
    {
        KvUpdate[]? kvUpdates = null;
        lock (_kvLock)
        {
            if (_pendingKvUpdates.Count > 0)
            {
                // Snapshot WITHOUT draining: each update is retransmitted on the next few
                // datagrams (UDP is lossy) and removed only once its send budget is exhausted.
                kvUpdates = new KvUpdate[_pendingKvUpdates.Count];
                for (var i = 0; i < _pendingKvUpdates.Count; i++)
                {
                    kvUpdates[i] = _pendingKvUpdates[i].Update;
                    _pendingKvUpdates[i].SendsLeft--;
                }

                _pendingKvUpdates.RemoveAll(p => p.SendsLeft <= 0);
            }
        }

        return new GossipMessage
        {
            Type = type,
            SenderId = _selfId,
            TargetId = targetId,
            Members = _membership.BuildMemberUpdates(),
            KvUpdates = kvUpdates
        };
    }
}
