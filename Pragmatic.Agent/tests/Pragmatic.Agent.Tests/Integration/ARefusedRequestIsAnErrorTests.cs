using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A request the daemon refuses is an error, not an empty answer. Read as an empty
///     answer, an unregistered reader would see an Agent with nobody connected, however many hosts were.
/// </summary>
public sealed class ARefusedRequestIsAnErrorTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private AgentSocketServer? _server;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(_store, "test-agent"));
        _server.Start();
        await Task.Delay(100);
        _store.Set("state/app:orders", "{}");
    }

    public Task DisposeAsync()
    {
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AnUnregisteredPrefixRead_Throws_NamingTheRefusal()
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();

        var read = () => connection.KvPrefixAsync("state/app:");

        (await read.Should().ThrowAsync<AgentRequestRefusedException>()).Which.Message.Should().Contain("Register");
    }

    [Fact]
    public async Task AnUnregisteredKeyRead_Throws()
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();

        var read = () => connection.KvGetAsync("state/app:orders");

        await read.Should().ThrowAsync<AgentRequestRefusedException>();
    }

    [Fact]
    public async Task AnUnregisteredWrite_Throws()
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();

        var write = () => connection.KvSetAsync("config/limit", "10");

        await write.Should().ThrowAsync<AgentRequestRefusedException>();
        _store.Get("config/limit").Should().BeNull();
    }

    /// <summary>The control: registered, the same read answers what is there.</summary>
    [Fact]
    public async Task ARegisteredPrefixRead_ReturnsTheEntries()
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();
        await connection.RegisterAsync("reader", "reader");

        var entries = await connection.KvPrefixAsync("state/app:");

        entries.Select(e => e.Key).Should().Contain("state/app:orders");
    }

    /// <summary>The control: a compare-and-swap conflict is an answer, not a refusal.</summary>
    [Fact]
    public async Task ACasConflict_IsAnAnswer_NotARefusal()
    {
        await using var connection = new AgentConnection(_pipeName);
        await connection.ConnectAsync();
        await connection.RegisterAsync("writer", "writer");
        await connection.KvSetAsync("config/limit", "10");

        var (_, conflict) = await connection.KvSetAsync("config/limit", "11", expectedVersion: 999);

        conflict.Should().BeTrue();
    }
}
