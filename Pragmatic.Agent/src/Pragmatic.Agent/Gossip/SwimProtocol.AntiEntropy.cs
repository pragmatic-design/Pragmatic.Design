namespace Pragmatic.Agent.Gossip;

/// <summary>
///     Anti-entropy: each Agent periodically pushes its whole KV state to one random member, so an update
///     the piggyback epidemic missed reaches every Agent anyway.
/// </summary>
/// <remarks>
///     <para>
///         Piggybacking is the fast path: an update rides the next few datagrams, and with a fixed budget
///         the epidemic can die out before it reaches everyone. This is what guarantees it does not stay
///         missed. The receiver applies the state with the same last-writer-wins rules, so pushing what a
///         peer already holds changes nothing there.
///     </para>
///     <para>
///         It is safe only because deletes leave tombstones: without them, an Agent that missed a delete
///         would push the old value back, and it would win.
///     </para>
/// </remarks>
internal sealed partial class SwimProtocol
{
    private static readonly TimeSpan AntiEntropyInterval = TimeSpan.FromSeconds(2);

    // Longer than the cluster needs to converge by anti-entropy many times over. A tombstone collected
    // while some Agent still holds the key would let that Agent bring it back.
    private static readonly TimeSpan TombstoneRetention = TimeSpan.FromMinutes(10);

    // Under MaxGossipPacketSize, with room for the member list and the HMAC tag.
    private const int StateChunkBudget = 48 * 1024;

    private Task? _antiEntropyTask;

    private async Task AntiEntropyLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AntiEntropyInterval, ct).ConfigureAwait(false);

                _kvStore.CollectTombstones(DateTimeOffset.UtcNow - TombstoneRetention);

                // Push and pull: this Agent's state goes to the peer, and the peer's comes back. With push
                // alone a lagging Agent is repaired only when another happens to pick it; with the pull
                // it repairs itself on every round of its own.
                var target = _membership.GetRandomAlive();
                if (target is not null)
                {
                    await SendStateAsync(target.Host, target.Port).ConfigureAwait(false);
                    await SendToAsync(new GossipMessage { Type = GossipMessageType.Pull, SenderId = _selfId },
                        target.Host, target.Port).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AgentLogger.Warn("Gossip", $"Anti-entropy error: {ex.Message}");
            }
        }
    }

    /// <summary>The whole KV state, in as many <see cref="GossipMessageType.Sync" /> datagrams as it takes.</summary>
    private async Task SendStateAsync(string host, int port)
    {
        foreach (var chunk in KvStateChunks.Of(_kvStore, StateChunkBudget))
        {
            var sync = new GossipMessage
            {
                Type = GossipMessageType.Sync,
                SenderId = _selfId,
                Members = _membership.BuildMemberUpdates(),
                KvUpdates = chunk,
            };
            await SendToAsync(sync, host, port).ConfigureAwait(false);
        }
    }
}
