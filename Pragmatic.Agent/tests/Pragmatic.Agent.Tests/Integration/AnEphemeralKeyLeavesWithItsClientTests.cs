using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A key written as ephemeral is the daemon's to delete when the client that wrote it goes:
///     what a process announces about itself, such as the address it serves on, must not outlive it.
/// </summary>
public sealed class AnEphemeralKeyLeavesWithItsClientTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private AgentSocketServer? _server;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(_store, "test-agent"));
        _server.Start();
        await Task.Delay(100);
    }

    public Task DisposeAsync()
    {
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TheClientDisconnects_ItsEphemeralKeyIsDeleted()
    {
        await using (var client = await RegisteredClientAsync("orders-a"))
        {
            (await client.KvSetEphemeralAsync("gateway/instances/orders/a", "http://a")).Should().BeTrue();
            _store.Get("gateway/instances/orders/a").Should().NotBeNull("it is there while the client is");
        }

        await UntilAsync(() => _store.Get("gateway/instances/orders/a") is null);

        _store.Get("gateway/instances/orders/a").Should().BeNull("its writer is gone");
    }

    /// <summary>The control: a plain key the same client wrote survives it.</summary>
    [Fact]
    public async Task TheClientDisconnects_ItsPlainKeyStays()
    {
        await using (var client = await RegisteredClientAsync("orders-a"))
        {
            await client.KvSetEphemeralAsync("gateway/instances/orders/a", "http://a");
            await client.KvSetAsync("config/orders/limit", "10");
        }

        await UntilAsync(() => _store.Get("gateway/instances/orders/a") is null);

        _store.Get("config/orders/limit")!.Value.Should().Be("10");
    }

    /// <summary>The control: another client's ephemeral key is not this one's to take away.</summary>
    [Fact]
    public async Task OneClientDisconnects_AnotherClientsEphemeralKeyStays()
    {
        await using var staying = await RegisteredClientAsync("orders-b");
        await staying.KvSetEphemeralAsync("gateway/instances/orders/b", "http://b");

        await using (var leaving = await RegisteredClientAsync("orders-a"))
            await leaving.KvSetEphemeralAsync("gateway/instances/orders/a", "http://a");

        await UntilAsync(() => _store.Get("gateway/instances/orders/a") is null);

        _store.Get("gateway/instances/orders/b")!.Value.Should().Be("http://b");
    }

    private async Task<AgentConnection> RegisteredClientAsync(string appId)
    {
        var client = new AgentConnection(_pipeName);
        await client.ConnectAsync();
        await client.RegisterAsync(appId, appId);
        return client;
    }

    /// <summary>The daemon learns of a disconnect from its read loop, a moment after the client closes.</summary>
    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }
}
