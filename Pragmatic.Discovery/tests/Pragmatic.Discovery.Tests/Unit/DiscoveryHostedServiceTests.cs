using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Tests the auto-registration <c>IHostedService</c> lifecycle. The service type is internal,
/// so it is resolved through the <see cref="IHostedService"/> collection wired by
/// <see cref="DiscoveryServiceExtensions.AddDiscovery"/>.
/// <para>
/// These tests only assert branches that are independent of the process-global
/// <c>AssemblyMetadataRegistry</c> state (whose entries cannot be removed once added),
/// so they remain deterministic regardless of execution order.
/// </para>
/// </summary>
public class DiscoveryHostedServiceTests
{
    private readonly InMemoryDiscoveryBackend _backend = new();
    private readonly CapturingLoggerProvider _logs = new();

    private IHostedService BuildHostedService(string environmentName, Action<DiscoveryOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddDiscovery(configure);
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(_logs);
        });
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));

        var descriptor = services.First(d => d.ServiceType == typeof(IDiscoveryBackend));
        services.Remove(descriptor);
        services.AddSingleton<IDiscoveryBackend>(_backend);

        var provider = services.BuildServiceProvider();
        return provider.GetServices<IHostedService>().Single();
    }

    [Fact]
    public async Task StartAsync_WithAutoRegisterDisabled_DoesNotRegisterTopology()
    {
        var hosted = BuildHostedService(
            Environments.Development,
            o => o.AutoRegisterOnStartup = false);

        await hosted.StartAsync(CancellationToken.None);

        var all = await _backend.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WithAutoRegisterDisabled_LogsSkipMessage()
    {
        var hosted = BuildHostedService(
            Environments.Development,
            o => o.AutoRegisterOnStartup = false);

        await hosted.StartAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("disabled"));
    }

    [Fact]
    public async Task StartAsync_InProductionWithThrowDisabled_LogsOperatorWarning()
    {
        var hosted = BuildHostedService(
            Environments.Production,
            o =>
            {
                o.ThrowOnValidationFailure = false;
                o.AutoRegisterOnStartup = false; // isolate the env warning from metadata lookup
            });

        await hosted.StartAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("ThrowOnValidationFailure"));
    }

    [Fact]
    public async Task StartAsync_InDevelopmentWithThrowDisabled_DoesNotLogOperatorWarning()
    {
        var hosted = BuildHostedService(
            Environments.Development,
            o =>
            {
                o.ThrowOnValidationFailure = false;
                o.AutoRegisterOnStartup = false;
            });

        await hosted.StartAsync(CancellationToken.None);

        _logs.Entries.Should().NotContain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("ThrowOnValidationFailure"));
    }

    [Fact]
    public async Task StartAsync_CompletesWithoutThrowing()
    {
        var hosted = BuildHostedService(Environments.Development, _ => { });

        var act = () => hosted.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_CompletesSuccessfully()
    {
        var hosted = BuildHostedService(Environments.Development, _ => { });

        var act = () => hosted.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
