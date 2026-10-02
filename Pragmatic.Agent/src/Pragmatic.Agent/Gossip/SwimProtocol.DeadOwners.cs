namespace Pragmatic.Agent.Gossip;

/// <summary>
///     What a dead Agent's clients announced leaves with it.
/// </summary>
/// <remarks>
///     <para>
///         An ephemeral entry records the Agent holding the client that wrote it. That Agent deletes it when
///         the client disconnects; an Agent that crashes, or loses its machine, never does. Every Agent that
///         learns of the death deletes the entries the dead one owned — whether it evicted the member itself
///         or heard of it through gossip — so the entries go within the suspect timeout plus a gossip round.
///     </para>
///     <para>
///         ⚠️ The death is the membership's verdict, and a partition can deliver it about an Agent that is
///         still running: its clients' entries are then deleted on the other Agents, and on its own too when
///         a delete arrives with a newer version than its copy. A client re-announces only when its rotation changes, so the entries stay
///         gone until then. That is the cost of the decision taken on the issue (owner plus death, no lease).
///     </para>
/// </remarks>
internal sealed partial class SwimProtocol
{
    private void RetireWhatADeadMemberOwned(ClusterMember member, MemberState previous)
    {
        if (member.State != MemberState.Dead)
            return;

        var deleted = _kvStore.DeleteOwnedBy(member.Id);
        if (deleted > 0)
            AgentLogger.Info("Gossip", $"Member {member.Id} is dead: deleted the {deleted} ephemeral key(s) it owned.");
    }
}
