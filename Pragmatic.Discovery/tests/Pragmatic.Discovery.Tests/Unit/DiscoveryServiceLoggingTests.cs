using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Verifies the logging behaviour of <c>DiscoveryService</c> (resolved via DI,
/// since the implementation is internal) using a capturing logger provider.
/// </summary>
public class DiscoveryServiceLoggingTests
{
    private readonly InMemoryDiscoveryBackend _backend = new();
    private readonly CapturingLoggerProvider _logs = new();

    private IDiscoveryService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDiscovery();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(_logs);
        });

        var descriptor = services.First(d => d.ServiceType == typeof(IDiscoveryBackend));
        services.Remove(descriptor);
        services.AddSingleton<IDiscoveryBackend>(_backend);

        return services.BuildServiceProvider().GetRequiredService<IDiscoveryService>();
    }

    private static HostTopologyInfo Module(string host, string module, string? db, string? provider)
        => new()
        {
            HostName = host,
            Modules = [new ModuleDeploymentInfo { ModuleName = module, DatabaseName = db, Provider = provider }]
        };

    [Fact]
    public async Task RegisterAsync_LogsInformationWithHostName()
    {
        var service = CreateService();

        await service.RegisterAsync(Module("HostA", "ModuleA", "Db", "InMemory"));

        _logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("HostA"));
    }

    [Fact]
    public async Task ValidateAsync_WhenValid_LogsInformationNotError()
    {
        var service = CreateService();
        await _backend.StoreAsync(Module("HostA", "ModuleA", "DbA", "InMemory"));

        var result = await service.ValidateAsync(Module("HostB", "ModuleB", "DbB", "InMemory"));

        result.IsValid.Should().BeTrue();
        _logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("validation passed"));
        _logs.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ValidateAsync_WithProviderMismatch_LogsErrorWithCode()
    {
        var service = CreateService();
        await _backend.StoreAsync(Module("HostA", "Shared", "SameDb", "SqlServer"));

        // Same module + same database, different provider => DISC002 Error.
        var result = await service.ValidateAsync(Module("HostB", "Shared", "SameDb", "Postgres"));

        result.Errors.Should().Contain(i => i.Code == "DISC002");
        _logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Message.Contains("DISC002"));
    }
}
