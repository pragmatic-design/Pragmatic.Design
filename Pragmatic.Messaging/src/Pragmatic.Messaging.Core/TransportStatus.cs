namespace Pragmatic.Messaging;

/// <summary>
///     Connection status of a message transport.
/// </summary>
public enum TransportStatus
{
    /// <summary>Not connected.</summary>
    Disconnected = 0,

    /// <summary>Connection attempt in progress.</summary>
    Connecting = 1,

    /// <summary>Connected and operational.</summary>
    Connected = 2,

    /// <summary>Connection lost or in error state.</summary>
    Faulted = 3
}
