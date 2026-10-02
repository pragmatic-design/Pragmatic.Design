using System.Collections.Concurrent;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     Thread-safe membership table for the SWIM gossip protocol.
///     Tracks known cluster members and their lifecycle states.
/// </summary>
internal sealed class MembershipTable(string selfId, TimeSpan? suspectTimeout = null)
{
    private readonly ConcurrentDictionary<string, ClusterMember> _members = new(StringComparer.Ordinal);

    private readonly TimeSpan _suspectTimeout = suspectTimeout ?? TimeSpan.FromSeconds(5);
    // Guards mutations to ClusterMember mutable fields (State, Incarnation, LastSeen, SuspectSince)
    // which are accessed from both gossip and probe threads concurrently.
    private readonly Lock _memberStateLock = new();

    /// <summary>Fired when a member joins or changes state.</summary>
    public event Action<ClusterMember, MemberState>? OnMemberChanged;

    /// <summary>Gets all alive members (excluding self).</summary>
    public IReadOnlyList<ClusterMember> GetAliveMembers()
    {
        return _members.Values
            .Where(m => m.Id != selfId && m.State == MemberState.Alive)
            .ToList();
    }

    /// <summary>Gets all known members (any state, excluding self).</summary>
    public IReadOnlyList<ClusterMember> GetAllMembers()
    {
        return _members.Values.Where(m => m.Id != selfId).ToList();
    }

    /// <summary>Gets a random alive member for ping target selection.</summary>
    public ClusterMember? GetRandomAlive()
    {
        var alive = GetAliveMembers();
        return alive.Count == 0 ? null : alive[Random.Shared.Next(alive.Count)];
    }

    /// <summary>Gets K random alive members for indirect probe (excluding target).</summary>
    public IReadOnlyList<ClusterMember> GetRandomProbers(string excludeId, int count = 3)
    {
        var candidates = _members.Values
            .Where(m => m.Id != selfId && m.Id != excludeId && m.State == MemberState.Alive)
            .ToList();

        if (candidates.Count <= count)
            return candidates;

        // Fisher-Yates partial shuffle
        for (var i = 0; i < count && i < candidates.Count; i++)
        {
            var j = Random.Shared.Next(i, candidates.Count);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        return candidates.Take(count).ToList();
    }

    /// <summary>
    ///     Applies a membership update from a gossip message.
    ///     Returns true if the state actually changed.
    /// </summary>
    public bool ApplyUpdate(MemberUpdate update)
    {
        if (update.Id == selfId)
            return false; // Don't update self from gossip

        var member = _members.GetOrAdd(update.Id, _ => new ClusterMember
        {
            Id = update.Id,
            Host = update.Host,
            Port = update.Port
        });

        lock (_memberStateLock)
        {
            // Incarnation-based conflict resolution: higher incarnation wins
            if (update.Incarnation < member.Incarnation)
                return false;

            if (update.Incarnation == member.Incarnation && update.State <= member.State)
                return false;

            // Authoritative update accepted. A member that restarted may rejoin with the same Id
            // but new network coordinates — refresh Host/Port so we don't keep pinging a stale
            // address. Only done for an accepted (newer) update so stale gossip can't rewrite them.
            // (Guarded by the state lock since BuildMemberUpdates / probing read these fields.)
            if (!string.IsNullOrEmpty(update.Host))
                member.Host = update.Host;
            if (update.Port != 0)
                member.Port = update.Port;

            var oldState = member.State;
            member.State = update.State;
            member.Incarnation = update.Incarnation;
            member.LastSeen = DateTimeOffset.UtcNow;

            if (update.State == MemberState.Suspect)
                member.SuspectSince = DateTimeOffset.UtcNow;
            else
                member.SuspectSince = null;

            if (oldState != update.State)
                OnMemberChanged?.Invoke(member, oldState);

            return true;
        }
    }

    /// <summary>
    ///     Adds a sender heard from directly, at the endpoint its datagram came from. A member already
    ///     known is left to the incarnation rules of <see cref="ApplyUpdate" />.
    /// </summary>
    /// <remarks>
    ///     This is how two Agents meet: the table never holds the Agent itself, so a Join names nobody,
    ///     and an Agent bound to <c>0.0.0.0</c> could not name its reachable address anyway — but the peer
    ///     that receives its datagram sees it.
    /// </remarks>
    public void AddHeardFrom(string memberId, string host, int port)
    {
        if (string.IsNullOrEmpty(memberId) || memberId == selfId)
            return;

        _members.TryAdd(memberId, new ClusterMember { Id = memberId, Host = host, Port = port });
    }

    /// <summary>Marks a member as alive (received a successful ping/ack).</summary>
    public void MarkAlive(string memberId)
    {
        if (_members.TryGetValue(memberId, out var member))
        {
            lock (_memberStateLock)
            {
                if (member.State == MemberState.Dead)
                    return; // Dead stays dead until re-join

                var old = member.State;
                member.State = MemberState.Alive;
                member.LastSeen = DateTimeOffset.UtcNow;
                member.SuspectSince = null;

                if (old != MemberState.Alive)
                    OnMemberChanged?.Invoke(member, old);
            }
        }
    }

    /// <summary>Marks a member as suspect (failed to respond to ping).</summary>
    public void MarkSuspect(string memberId)
    {
        if (_members.TryGetValue(memberId, out var member))
        {
            lock (_memberStateLock)
            {
                if (member.State != MemberState.Alive) return;

                member.State = MemberState.Suspect;
                member.SuspectSince = DateTimeOffset.UtcNow;
                OnMemberChanged?.Invoke(member, MemberState.Alive);
            }
        }
    }

    /// <summary>Promotes suspects to dead if they've been suspect too long.</summary>
    public void EvictStaleMembers()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var member in _members.Values)
        {
            lock (_memberStateLock)
            {
                if (member.State == MemberState.Suspect &&
                    member.SuspectSince.HasValue &&
                    now - member.SuspectSince.Value > _suspectTimeout)
                {
                    member.State = MemberState.Dead;
                    member.LastSeen = now;
                    OnMemberChanged?.Invoke(member, MemberState.Suspect);
                }
            }

            // Remove dead members after 30s to free memory.
            // Read State under lock; TryRemove is atomic on ConcurrentDictionary.
            bool isDead;
            DateTimeOffset lastSeen;
            lock (_memberStateLock)
            {
                isDead = member.State == MemberState.Dead;
                lastSeen = member.LastSeen;
            }

            if (isDead && now - lastSeen > TimeSpan.FromSeconds(30))
                _members.TryRemove(member.Id, out _);
        }
    }

    /// <summary>Builds membership updates for piggybacking on gossip messages.</summary>
    public MemberUpdate[] BuildMemberUpdates()
    {
        return _members.Values
            .Select(m => new MemberUpdate
            {
                Id = m.Id,
                Host = m.Host,
                Port = m.Port,
                State = m.State,
                Incarnation = m.Incarnation
            })
            .ToArray();
    }

    /// <summary>Total count of known members (any state).</summary>
    public int Count => _members.Count;
}
