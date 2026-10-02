using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Client.Stores;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Socket;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Integration;

/// <summary>
///     A setting written through the Agent reaches a host's typed options, at startup and at
///     runtime: <c>UseAgent()</c> puts the Agent-backed store into the host's configuration.
/// </summary>
public sealed class ASettingWrittenThroughTheAgentReachesTheOptionsTests : IAsyncLifetime
{
    private readonly string _pipeName = $"pragmatic-test-{Guid.NewGuid():N}";
    private readonly KvStore _store = new();
    private AgentSocketServer? _server;
    private KvChangeBroadcaster? _broadcaster;

    public async Task InitializeAsync()
    {
        _server = new AgentSocketServer(_pipeName, new AgentMessageHandler(_store, "test-agent"));
        _server.Start();
        // As the daemon does: without it no client hears a change, and a watch can only poll.
        _broadcaster = new KvChangeBroadcaster(_store, _server);
        await Task.Delay(100);
    }

    public Task DisposeAsync()
    {
        _broadcaster?.Dispose();
        _server?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AWatchTakenBeforeTheConnection_HearsAChangeWrittenAfterIt()
    {
        await using var connection = new AgentConnection(_pipeName);
        var store = new AgentConfigurationStore(connection);
        using var watching = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var heard = FirstChangeAsync(store.WatchAsync("Warehouse", watching.Token), "Warehouse:DefaultReorderThreshold");

        await connection.ConnectAsync();
        await connection.RegisterAsync("host", "host");
        await WriteAsync("config/Warehouse:DefaultReorderThreshold", "7");

        (await heard).Should().Be("7");
    }

    [Fact]
    public async Task AHostWithUseAgent_SeesInItsOptions_AValueWrittenBeforeItConnected_AndOneWrittenAfter()
    {
        await WriteAsync("config/Warehouse:DefaultReorderThreshold", "5");
        using var host = HostWithAgent();
        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptionsMonitor<ReorderSettings>>();

            await UntilAsync(() => options.CurrentValue.DefaultReorderThreshold == 5);
            options.CurrentValue.DefaultReorderThreshold.Should().Be(5, "written through the Agent before the host connected");

            await WriteAsync("config/Warehouse:DefaultReorderThreshold", "9");
            await UntilAsync(() => options.CurrentValue.DefaultReorderThreshold == 9);
            options.CurrentValue.DefaultReorderThreshold.Should().Be(9, "changed through the Agent while the host runs");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>The control: a key outside <c>config/</c> is not configuration.</summary>
    [Fact]
    public async Task AKeyOutsideConfig_ChangesNothing()
    {
        using var host = HostWithAgent();
        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptionsMonitor<ReorderSettings>>();
            var connected = host.Services.GetRequiredService<AgentConnection>();
            await UntilAsync(() => connected.IsConnected);

            await WriteAsync("flags/Warehouse:DefaultReorderThreshold", "11");
            await Task.Delay(500);

            options.CurrentValue.DefaultReorderThreshold.Should().Be(ReorderSettings.Default);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost HostWithAgent()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Pragmatic:Agent:SocketPath"] = _pipeName;
        new Builder(builder).UseAgent();
        builder.Services.AddOptions<ReorderSettings>().Bind(builder.Configuration.GetSection("Warehouse"));
        return builder.Build();
    }

    private async Task WriteAsync(string key, string value)
    {
        await using var writer = new AgentConnection(_pipeName);
        await writer.ConnectAsync();
        await writer.RegisterAsync("operator", "operator");
        await writer.KvSetAsync(key, value);
    }

    private static async Task<string?> FirstChangeAsync(
        IAsyncEnumerable<Pragmatic.Configuration.ConfigurationChange> changes, string key)
    {
        try
        {
            await foreach (var change in changes)
            {
                if (change.Key == key)
                    return change.NewValue;
            }
        }
        catch (OperationCanceledException)
        {
            // The watch timed out: nothing was heard.
        }

        return "<nothing heard>";
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    public sealed class ReorderSettings
    {
        public const int Default = 3;
        public int DefaultReorderThreshold { get; set; } = Default;
    }

    private sealed class Builder(HostApplicationBuilder host) : IPragmaticBuilder
    {
        public IServiceCollection Services => host.Services;
        public IConfiguration Configuration => host.Configuration;
        public IHostEnvironment Environment => host.Environment;
    }
}
