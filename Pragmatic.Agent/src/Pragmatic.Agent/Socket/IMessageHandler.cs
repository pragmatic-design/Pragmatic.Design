using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Handles incoming messages from a connected client.
///     Returns a response message, or null if no response is needed.
/// </summary>
internal interface IMessageHandler
{
    Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message);

    /// <summary>
    ///     Called by the socket server when a client connection ends (EOF, error, or timeout).
    ///     Lets the handler retire per-connection state — notably the <c>state/app:</c> roster entry
    ///     the registered app owned. No-op for connections that never registered.
    /// </summary>
    void OnClientDisconnected(ClientConnection client);
}
