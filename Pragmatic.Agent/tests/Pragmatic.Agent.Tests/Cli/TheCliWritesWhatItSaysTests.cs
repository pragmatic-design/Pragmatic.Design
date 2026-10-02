using Pragmatic.Agent.Cli;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Cli;

/// <summary>
///     The CLI registers before it asks: the daemon refuses every KV operation from a client that
///     has not, so `config set` printed "Set … (v-1)", exited 0 and wrote nothing, and every read came back
///     empty.
/// </summary>
public sealed class TheCliWritesWhatItSaysTests : IAsyncLifetime
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
    public async Task ConfigSet_WritesTheKey_AndExitsZero()
    {
        var exit = await CliHandler.ExecuteAsync(["config", "set", "Warehouse:DefaultReorderThreshold", "10", "--socket", _pipeName]);

        exit.Should().Be(0);
        (_store.Get("config/Warehouse:DefaultReorderThreshold")?.Value ?? "<missing>").Should().Be("10", "the CLI said it set it");
    }

    [Fact]
    public async Task MaintenanceOn_WritesTheToggle()
    {
        var exit = await CliHandler.ExecuteAsync(["maintenance", "on", "--socket", _pipeName]);

        exit.Should().Be(0);
        (_store.Get("state/gateway/maintenance")?.Value ?? "<missing>").Should().Be("true");
    }

    /// <summary>The control: the CLI's own registration is gone when it exits, and leaves no roster entry.</summary>
    [Fact]
    public async Task AfterACommand_TheCliIsNotOnTheRoster()
    {
        await CliHandler.ExecuteAsync(["config", "set", "k", "v", "--socket", _pipeName]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_store.GetByPrefix("state/app:").Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        _store.GetByPrefix("state/app:").Should().BeEmpty();
    }
}
