using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     End-to-end tests: Agent socket server ↔ Agent client via named pipe.
///     Validates the full request/response cycle over IPC.
/// </summary>
public class AgentClientIntegrationTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _kvStore = new();
    private AgentSocketServer? _server;
    private AgentConnection? _client;

    public async Task InitializeAsync()
    {
        var handler = new AgentMessageHandler(_kvStore, "test-agent");
        _server = new AgentSocketServer(_pipeName, handler);
        _server.Start();

        // Small delay to let server start listening
        await Task.Delay(100);

        _client = new AgentConnection(_pipeName);
        await _client.ConnectAsync();

        // Security: must register before KV operations
        await _client.RegisterAsync("test-integration", "IntegrationTest");
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _server?.Dispose();
    }

    [Fact]
    public async Task Register_Succeeds()
    {
        var result = await _client!.RegisterAsync("test-app", "Test Application", "1.0.0");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task KvSet_And_KvGet_Roundtrip()
    {
        var (version, conflict) = await _client!.KvSetAsync("config/host", "localhost");
        conflict.Should().BeFalse();
        version.Should().BeGreaterThan(0);

        var (value, _, found) = await _client.KvGetAsync("config/host");
        found.Should().BeTrue();
        value.Should().Be("localhost");
    }

    [Fact]
    public async Task KvDelete_RemovesEntry()
    {
        await _client!.KvSetAsync("temp/key", "value");

        var deleted = await _client.KvDeleteAsync("temp/key");
        deleted.Should().BeTrue();

        var (_, _, found) = await _client.KvGetAsync("temp/key");
        found.Should().BeFalse();
    }

    [Fact]
    public async Task KvPrefix_ReturnsMatchingEntries()
    {
        await _client!.KvSetAsync("config/a", "1");
        await _client.KvSetAsync("config/b", "2");
        await _client.KvSetAsync("flags/x", "true");

        var entries = await _client.KvPrefixAsync("config/");
        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task KvSet_CAS_ConflictDetected()
    {
        var (v1, _) = await _client!.KvSetAsync("cas-key", "original");

        // Set with wrong expected version
        var (_, conflict) = await _client.KvSetAsync("cas-key", "updated", expectedVersion: v1 + 999);
        conflict.Should().BeTrue();

        // Original value preserved
        var (value, _, _) = await _client.KvGetAsync("cas-key");
        value.Should().Be("original");
    }

    [Fact]
    public async Task KvSet_CAS_SucceedsWithCorrectVersion()
    {
        var (v1, _) = await _client!.KvSetAsync("cas-key", "original");

        var (v2, conflict) = await _client.KvSetAsync("cas-key", "updated", expectedVersion: v1);
        conflict.Should().BeFalse();
        v2.Should().BeGreaterThan(v1);

        var (value, _, _) = await _client.KvGetAsync("cas-key");
        value.Should().Be("updated");
    }

    [Fact]
    public async Task Heartbeat_Succeeds()
    {
        await _client!.RegisterAsync("heartbeat-app", "Test");

        // Should not throw
        await _client.HeartbeatAsync("heartbeat-app", "healthy");
    }

    [Fact]
    public async Task MultipleOperations_Sequential()
    {
        // Validates connection stability across many operations
        for (var i = 0; i < 50; i++)
        {
            await _client!.KvSetAsync($"batch/key-{i}", $"value-{i}");
        }

        var entries = await _client!.KvPrefixAsync("batch/");
        entries.Should().HaveCount(50);
    }
}
