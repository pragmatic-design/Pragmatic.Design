using System.Text.Json;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Routes incoming socket messages to the appropriate handler (KV, register, heartbeat).
///     Clients must Register before accessing KV operations (unauthenticated clients can only heartbeat).
/// </summary>
/// <param name="kvStore">The store.</param>
/// <param name="agentId">
///     This Agent's id, recorded as the owner of what its clients write as theirs alone — ephemeral keys and
///     roster entries — so the other Agents can delete it when this one dies.
/// </param>
internal sealed class AgentMessageHandler(KvStore kvStore, string agentId) : IMessageHandler
{
    /// <summary>
    ///     Raised (with the instance id) after an instance registers and its roster entry is written. The
    ///     daemon wires this to the command dispatch pump so any command that arrived for the instance while
    ///     it was disconnected is flushed to it on (re)connect. Commands are addressed to instances.
    /// </summary>
    public Action<string>? InstanceRegistered { get; set; }

    public Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message)
    {
        // Security (fail-CLOSED): everything requires prior registration EXCEPT the small set of
        // pre-auth message types below. A new/unknown message type therefore denies by default
        // instead of silently bypassing the registration check.
        if (!IsPreAuthMessageType(message.Type) && client.AppId is null)
        {
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Unauthorized: client must Register before this operation"));
        }

        return message.Type switch
        {
            MessageType.Register => HandleRegister(client, message),
            MessageType.Heartbeat => HandleHeartbeat(client, message),
            MessageType.KvGet => HandleKvGet(message),
            MessageType.KvSet => HandleKvSet(client, message),
            MessageType.KvDelete => HandleKvDelete(message),
            MessageType.KvPrefix => HandleKvPrefix(message),
            _ => Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, $"Unknown message type: {message.Type}"))
        };
    }

    private Task<AgentMessage?> HandleRegister(ClientConnection client, AgentMessage message)
    {
        var payload = Deserialize<RegisterPayload>(message);
        if (payload is null)
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Invalid register payload"));

        // One entry per instance: the host's own id when it sends one, else one for this
        // connection. Keyed by app alone, a second instance overwrote the first and the first to leave
        // deleted both.
        var instanceId = string.IsNullOrWhiteSpace(payload.InstanceId) ? Guid.NewGuid().ToString("N") : payload.InstanceId;
        client.AppId = payload.AppId;
        client.InstanceId = instanceId;
        client.RosterKey = HostRosterKeys.Key(payload.AppId, instanceId);

        // Persist the host roster entry so GetAllHostsAsync sees a real roster and state/app: changes
        // gossip cluster-wide. Without this write the roster was permanently empty.
        var now = DateTimeOffset.UtcNow;
        var descriptor = new HostDescriptorPayload
        {
            AppId = payload.AppId,
            AppName = payload.AppName,
            InstanceId = instanceId,
            Version = payload.Version,
            HostType = payload.HostType ?? "Tenant",
            State = "Ready",
            ProcessId = payload.ProcessId,
            StartedAt = payload.StartedAt ?? now,
            LastHeartbeat = now,
        };
        // Owned like an ephemeral key: it leaves with the connection, so it must also leave with this Agent.
        kvStore.Set(client.RosterKey, JsonSerializer.Serialize(descriptor), owner: agentId);

        // Deliver any commands that were queued while this app was disconnected.
        InstanceRegistered?.Invoke(instanceId);

        return Task.FromResult<AgentMessage?>(SuccessResponse(message.Id));
    }

    private Task<AgentMessage?> HandleHeartbeat(ClientConnection client, AgentMessage message)
    {
        // Refresh the roster entry's lastHeartbeat so stale-host detection has a real timestamp, and
        // fold in the app's reported lifecycle state (maintenance/draining/…) so the descriptor — and
        // thus every consumer reading state/app: (e.g. the Gateway) — reflects it via one source.
        // Keyed by the REGISTERED identity (client.AppId), never the payload's appId, so an
        // unregistered client cannot forge or refresh another app's roster entry (fail-closed).
        if (client.RosterKey is { } rosterKey)
            RefreshHeartbeat(rosterKey, Deserialize<HeartbeatPayload>(message)?.State);

        return Task.FromResult<AgentMessage?>(SuccessResponse(message.Id));
    }

    private void RefreshHeartbeat(string key, string? reportedState)
    {
        var existing = kvStore.Get(key);
        if (existing?.Value is null)
            return; // No roster entry (never registered on this daemon) — nothing to refresh.

        var descriptor = TryDeserializeDescriptor(existing.Value);
        if (descriptor is null)
            return;

        var refreshed = new HostDescriptorPayload
        {
            AppId = descriptor.AppId,
            AppName = descriptor.AppName,
            InstanceId = descriptor.InstanceId,
            Version = descriptor.Version,
            HostType = descriptor.HostType,
            // Reported state wins when the app sends one; otherwise keep what the descriptor held.
            State = string.IsNullOrEmpty(reportedState) ? descriptor.State : reportedState!,
            ProcessId = descriptor.ProcessId,
            StartedAt = descriptor.StartedAt,
            LastHeartbeat = DateTimeOffset.UtcNow,
        };
        kvStore.Set(key, JsonSerializer.Serialize(refreshed), owner: agentId);
    }

    /// <inheritdoc />
    public void OnClientDisconnected(ClientConnection client)
    {
        // Retire this instance's roster entry, and only it: another instance of the same app keeps its own.
        // A never-registered connection has no key → no-op.
        if (client.RosterKey is { } rosterKey)
            kvStore.Delete(rosterKey);

        // What the client announced about its own process goes with it (KvSetPayload.Ephemeral).
        foreach (var key in client.EphemeralKeys)
            kvStore.Delete(key);
    }

    private static HostDescriptorPayload? TryDeserializeDescriptor(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HostDescriptorPayload>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<AgentMessage?> HandleKvGet(AgentMessage message)
    {
        var payload = Deserialize<KvGetPayload>(message);
        if (payload is null)
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Invalid KvGet payload"));

        // KvStore.Get already decrypts secret/* values (encryption is owned by the store, at rest).
        var entry = kvStore.Get(payload.Key);

        var result = new KvEntryPayload
        {
            Key = payload.Key,
            Value = entry?.Value,
            Version = entry?.Version ?? 0,
            Found = entry is not null
        };

        return Task.FromResult<AgentMessage?>(PayloadResponse(message.Id, result));
    }

    private Task<AgentMessage?> HandleKvSet(ClientConnection client, AgentMessage message)
    {
        var payload = Deserialize<KvSetPayload>(message);
        if (payload is null)
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Invalid KvSet payload"));

        // KvStore encrypts secret/* values at rest; pass plaintext through.
        var (version, casConflict) = kvStore.Set(payload.Key, payload.Value, payload.ExpectedVersion,
            payload.Ephemeral ? agentId : null);
        if (payload.Ephemeral && !casConflict)
            client.AddEphemeralKey(payload.Key);
        var result = new KvSetResultPayload { Version = version, CasConflict = casConflict };

        return Task.FromResult<AgentMessage?>(new AgentMessage
        {
            Type = MessageType.Response,
            Id = message.Id,
            Success = !casConflict,
            Payload = JsonSerializer.SerializeToElement(result)
        });
    }

    private Task<AgentMessage?> HandleKvDelete(AgentMessage message)
    {
        var payload = Deserialize<KvDeletePayload>(message);
        if (payload is null)
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Invalid KvDelete payload"));

        kvStore.Delete(payload.Key);
        return Task.FromResult<AgentMessage?>(SuccessResponse(message.Id));
    }

    private Task<AgentMessage?> HandleKvPrefix(AgentMessage message)
    {
        var payload = Deserialize<KvPrefixPayload>(message);
        if (payload is null)
            return Task.FromResult<AgentMessage?>(ErrorResponse(message.Id, "Invalid KvPrefix payload"));

        var entries = kvStore.GetByPrefix(payload.Prefix);
        var result = new KvEntriesPayload
        {
            // Security: exclude secret/* values from prefix listing (use KvGet for individual access)
            Entries = entries.Select(e => new KvEntryPayload
            {
                Key = e.Key,
                Value = KvStore.IsSecretKey(e.Key) ? "***" : e.Value,
                Version = e.Version,
                Found = true
            }).ToArray()
        };

        return Task.FromResult<AgentMessage?>(PayloadResponse(message.Id, result));
    }

    private static T? Deserialize<T>(AgentMessage message) where T : class
    {
        if (message.Payload is null)
            return null;

        try
        {
            return message.Payload.Value.Deserialize<T>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed payload — caller will return an error response.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Called on a disposed or incompatible JsonElement.
            return null;
        }
        // All other exceptions (OOM, StackOverflow, etc.) propagate to the caller.
    }

    private static AgentMessage SuccessResponse(string? id) => new()
    {
        Type = MessageType.Response,
        Id = id,
        Success = true
    };

    private static AgentMessage ErrorResponse(string? id, string error) => new()
    {
        Type = MessageType.Response,
        Id = id,
        Success = false,
        Error = error
    };

    private static AgentMessage PayloadResponse<T>(string? id, T payload) => new()
    {
        Type = MessageType.Response,
        Id = id,
        Success = true,
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    /// <summary>
    ///     Explicit allow-list of message types an unregistered client may send.
    ///     Anything not listed here is denied until the client has Registered (fail-closed):
    ///     adding a new message type does not silently grant pre-auth access.
    /// </summary>
    private static bool IsPreAuthMessageType(MessageType type)
        => type is MessageType.Register or MessageType.Heartbeat;
}
