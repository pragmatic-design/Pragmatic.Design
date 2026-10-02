namespace Pragmatic.Agent.Protocol;

/// <summary>
///     Wire message types for the Agent socket protocol.
///     App→Agent (requests) and Agent→App (notifications/responses).
/// </summary>
public enum MessageType
{
    // ═══ App → Agent (requests) ═══

    /// <summary>App registers itself with the Agent on startup.</summary>
    Register = 1,

    /// <summary>Periodic health heartbeat from app.</summary>
    Heartbeat = 2,

    /// <summary>App reports its lifecycle state change.</summary>
    StateReport = 3,

    // ═══ KV Operations (App → Agent) ═══

    /// <summary>Get a single KV entry by key.</summary>
    KvGet = 10,

    /// <summary>Set a KV entry (with optional CAS version).</summary>
    KvSet = 11,

    /// <summary>Delete a KV entry.</summary>
    KvDelete = 12,

    /// <summary>Get all entries matching a key prefix.</summary>
    KvPrefix = 13,

    /// <summary>Subscribe to changes on a key prefix.</summary>
    KvWatch = 14,

    /// <summary>Unsubscribe from a KV watch.</summary>
    KvUnwatch = 15,

    // ═══ Agent → App (notifications) ═══

    /// <summary>Response to any request.</summary>
    Response = 100,

    /// <summary>KV entry changed (pushed to watchers).</summary>
    KvChanged = 101,

    /// <summary>Command from Agent to App (maintenance, drain, etc.).</summary>
    Command = 102,

    /// <summary>Agent notifies app of a state transition.</summary>
    StateChange = 103,

    /// <summary>Agent is shutting down.</summary>
    AgentShutdown = 104
}
