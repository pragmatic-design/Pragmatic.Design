using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Client;

/// <summary>
///     The Agent refused a request — typically because the client has not registered, which the daemon
///     requires for everything but registering and heartbeating.
/// </summary>
/// <remarks>
///     A refusal is not an empty answer: read as one, an unregistered roster read would see nobody
///     connected and a key read nothing stored, and neither would be true. A caller that wants to degrade when
///     the Agent is unavailable decides it on <see cref="AgentConnection.IsConnected" />, not on an empty
///     result; a refusal while connected is a fault to surface.
/// </remarks>
public sealed class AgentRequestRefusedException(MessageType request, string reason)
    : InvalidOperationException($"The Agent refused {request}: {reason}")
{
    /// <summary>The request that was refused.</summary>
    public MessageType Request { get; } = request;

    /// <summary>What the Agent said.</summary>
    public string Reason { get; } = reason;
}
