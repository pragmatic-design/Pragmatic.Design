using System.Text.Json;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Socket;

/// <summary>
///     What a client writes as its own records the Agent that holds it, so the other Agents can
///     delete it when that Agent dies. A plain write records nobody.
/// </summary>
public sealed class WhatAClientOwnsIsRecordedTests
{
    private const string AgentId = "agent-holding-the-client";
    private readonly KvStore _store = new();
    private readonly AgentMessageHandler _handler;

    public WhatAClientOwnsIsRecordedTests() => _handler = new AgentMessageHandler(_store, AgentId);

    [Fact]
    public async Task AnEphemeralWrite_IsOwnedByThisAgent_AndAPlainOneByNobody()
    {
        var client = Registered();

        await _handler.HandleAsync(client, Message(MessageType.KvSet,
            new KvSetPayload { Key = "gateway/instances/orders/i1", Value = "{}", Ephemeral = true }));
        await _handler.HandleAsync(client, Message(MessageType.KvSet,
            new KvSetPayload { Key = "config/orders/limit", Value = "10" }));

        _store.Get("gateway/instances/orders/i1")!.Owner.Should().Be(AgentId);
        _store.Get("config/orders/limit")!.Owner.Should().BeNull("the control: a plain key outlives the Agent");
    }

    [Fact]
    public async Task TheRosterEntry_IsOwnedByThisAgent_BeforeAndAfterAHeartbeat()
    {
        var client = new ClientConnection(new MemoryStream(), new NoOpHandler(), _ => { });

        await _handler.HandleAsync(client, Message(MessageType.Register,
            new RegisterPayload { AppId = "orders", AppName = "Orders", InstanceId = "i1", ProcessId = 1 }));
        _store.Get(client.RosterKey!)!.Owner.Should().Be(AgentId,
            "the entry leaves with the connection, so it must also leave with the Agent");

        await _handler.HandleAsync(client, Message(MessageType.Heartbeat, new HeartbeatPayload { AppId = "orders", Health = "healthy", State = "Ready" }));
        _store.Get(client.RosterKey!)!.Owner.Should().Be(AgentId, "a heartbeat rewrites the entry and keeps the owner");
    }

    private static ClientConnection Registered()
    {
        var client = new ClientConnection(new MemoryStream(), new NoOpHandler(), _ => { });
        client.AppId = "orders";
        return client;
    }

    private static AgentMessage Message<T>(MessageType type, T payload) => new()
    {
        Type = type,
        Id = Guid.NewGuid().ToString(),
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    private sealed class NoOpHandler : IMessageHandler
    {
        public Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message)
            => Task.FromResult<AgentMessage?>(null);

        public void OnClientDisconnected(ClientConnection client) { }
    }
}
