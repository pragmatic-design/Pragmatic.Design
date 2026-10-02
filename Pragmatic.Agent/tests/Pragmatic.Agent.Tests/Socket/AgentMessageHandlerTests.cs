using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Socket;
using Xunit;

namespace Pragmatic.Agent.Tests.Socket;

public class AgentMessageHandlerTests
{
    private readonly KvStore _kvStore = new();
    private readonly AgentMessageHandler _handler;

    public AgentMessageHandlerTests()
    {
        _handler = new AgentMessageHandler(_kvStore, "test-agent");
    }

    [Fact]
    public async Task Register_SetsAppIdOnClient()
    {
        var client = CreateClient();
        var message = CreateMessage(MessageType.Register, new RegisterPayload
        {
            AppId = "booking-1",
            AppName = "Booking API",
            ProcessId = 1234
        });

        var response = await _handler.HandleAsync(client, message);

        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        client.AppId.Should().Be("booking-1");
    }

    [Fact]
    public async Task KvGet_ExistingKey_ReturnsValue()
    {
        _kvStore.Set("config/host", "localhost");

        var message = CreateMessage(MessageType.KvGet, new KvGetPayload { Key = "config/host" });
        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);

        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();

        var result = response.Payload!.Value.Deserialize<KvEntryPayload>();
        result!.Found.Should().BeTrue();
        result.Value.Should().Be("localhost");
    }

    [Fact]
    public async Task KvGet_MissingKey_ReturnsNotFound()
    {
        var message = CreateMessage(MessageType.KvGet, new KvGetPayload { Key = "missing" });
        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);

        var result = response!.Payload!.Value.Deserialize<KvEntryPayload>();
        result!.Found.Should().BeFalse();
    }

    [Fact]
    public async Task KvSet_WritesValue()
    {
        var message = CreateMessage(MessageType.KvSet, new KvSetPayload
        {
            Key = "config/port",
            Value = "8080"
        });

        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);
        response!.Success.Should().BeTrue();

        _kvStore.Get("config/port")!.Value.Should().Be("8080");
    }

    [Fact]
    public async Task KvSet_CASConflict_ReturnsFalse()
    {
        _kvStore.Set("key", "old");

        var message = CreateMessage(MessageType.KvSet, new KvSetPayload
        {
            Key = "key",
            Value = "new",
            ExpectedVersion = 999 // Wrong version
        });

        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);
        response!.Success.Should().BeFalse();

        var result = response.Payload!.Value.Deserialize<KvSetResultPayload>();
        result!.CasConflict.Should().BeTrue();
    }

    [Fact]
    public async Task KvDelete_RemovesKey()
    {
        _kvStore.Set("temp", "value");

        var message = CreateMessage(MessageType.KvDelete, new KvDeletePayload { Key = "temp" });
        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);

        response!.Success.Should().BeTrue();
        _kvStore.Get("temp").Should().BeNull();
    }

    [Fact]
    public async Task KvPrefix_ReturnsMatchingEntries()
    {
        _kvStore.Set("config/a", "1");
        _kvStore.Set("config/b", "2");
        _kvStore.Set("flags/x", "true");

        var message = CreateMessage(MessageType.KvPrefix, new KvPrefixPayload { Prefix = "config/" });
        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);

        var result = response!.Payload!.Value.Deserialize<KvEntriesPayload>();
        result!.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Heartbeat_ReturnsSuccess()
    {
        var message = CreateMessage(MessageType.Heartbeat, new HeartbeatPayload
        {
            AppId = "app-1",
            Health = "healthy"
        });

        var response = await _handler.HandleAsync(CreateClient(), message);
        response!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownMessageType_ReturnsError()
    {
        // A REGISTERED client reaches the unknown-type path. (Unregistered clients are now rejected
        // earlier with an Unauthorized error — fail-closed — before the type is even inspected.)
        var message = new AgentMessage { Type = (MessageType)999, Id = "test" };
        var response = await _handler.HandleAsync(CreateRegisteredClient(), message);

        response!.Success.Should().BeFalse();
        response.Error.Should().Contain("Unknown");
    }

    private static AgentMessage CreateMessage<T>(MessageType type, T payload) => new()
    {
        Type = type,
        Id = Guid.NewGuid().ToString(),
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    private static ClientConnection CreateClient()
    {
        // Create a dummy client with a MemoryStream (won't actually send)
        return new ClientConnection(new MemoryStream(), new NoOpHandler(), _ => { });
    }

    /// <summary>Create a client that has already registered (authorized for KV ops).</summary>
    private static ClientConnection CreateRegisteredClient()
    {
        var client = CreateClient();
        client.AppId = "test-app";
        return client;
    }

    private sealed class NoOpHandler : IMessageHandler
    {
        public Task<AgentMessage?> HandleAsync(ClientConnection client, AgentMessage message)
            => Task.FromResult<AgentMessage?>(null);

        public void OnClientDisconnected(ClientConnection client) { }
    }
}
