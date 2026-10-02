using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Tests DiscoveryService through IDiscoveryService (resolved via DI, since the class is internal).
/// </summary>
public class DiscoveryServiceTests
{
    private readonly InMemoryDiscoveryBackend _backend = new();

    private IDiscoveryService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDiscovery();
        services.AddLogging();

        // Replace backend with our controlled instance
        var descriptor = services.First(d => d.ServiceType == typeof(IDiscoveryBackend));
        services.Remove(descriptor);
        services.AddSingleton<IDiscoveryBackend>(_backend);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDiscoveryService>();
    }

    private static HostTopologyInfo CreateTopology(string hostName, params string[] moduleNames)
    {
        return new HostTopologyInfo
        {
            HostName = hostName,
            Modules = moduleNames.Select(m => new ModuleDeploymentInfo { ModuleName = m }).ToList()
        };
    }

    [Fact]
    public async Task RegisterAsync_StoresTopology()
    {
        var service = CreateService();
        var topology = CreateTopology("HostA", "ModuleA");

        await service.RegisterAsync(topology);

        var stored = await _backend.GetByHostNameAsync("HostA");
        stored.Should().NotBeNull();
        stored!.HostName.Should().Be("HostA");
    }

    [Fact]
    public async Task GetAllHostsAsync_ReturnsAllRegistered()
    {
        var service = CreateService();

        await service.RegisterAsync(CreateTopology("HostA", "ModuleA"));
        await service.RegisterAsync(CreateTopology("HostB", "ModuleB"));

        var all = await service.GetAllHostsAsync();

        all.Should().HaveCount(2);
        all.Select(h => h.HostName).Should().BeEquivalentTo("HostA", "HostB");
    }

    [Fact]
    public async Task GetAllHostsAsync_WithNoRegistrations_ReturnsEmpty()
    {
        var service = CreateService();

        var all = await service.GetAllHostsAsync();

        all.Should().BeEmpty();
    }

    [Fact]
    public async Task FindHostsForModuleAsync_ReturnsHostsWithModule()
    {
        var service = CreateService();

        await service.RegisterAsync(CreateTopology("HostA", "ModuleA", "SharedModule"));
        await service.RegisterAsync(CreateTopology("HostB", "SharedModule"));
        await service.RegisterAsync(CreateTopology("HostC", "ModuleC"));

        var hosts = await service.FindHostsForModuleAsync("SharedModule");

        hosts.Should().HaveCount(2);
        hosts.Select(h => h.HostName).Should().BeEquivalentTo("HostA", "HostB");
    }

    [Fact]
    public async Task FindHostsForModuleAsync_WithNonExistentModule_ReturnsEmpty()
    {
        var service = CreateService();

        await service.RegisterAsync(CreateTopology("HostA", "ModuleA"));

        var hosts = await service.FindHostsForModuleAsync("NonExistent");

        hosts.Should().BeEmpty();
    }

    [Fact]
    public async Task FindHostsForModuleAsync_IsCaseSensitiveOnModuleName()
    {
        var service = CreateService();

        await service.RegisterAsync(CreateTopology("HostA", "ModuleA"));

        var hosts = await service.FindHostsForModuleAsync("modulea");

        // Module name comparison is case-sensitive (Ordinal)
        hosts.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithNoConflicts_ReturnsValid()
    {
        var service = CreateService();

        await service.RegisterAsync(CreateTopology("HostA", "ModuleA"));
        var incoming = CreateTopology("HostB", "ModuleB");

        var result = await service.ValidateAsync(incoming);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ReturnsValidationResult()
    {
        var service = CreateService();

        // Set up a conflict: same module, different databases
        await _backend.StoreAsync(new HostTopologyInfo
        {
            HostName = "HostA",
            Modules = [new ModuleDeploymentInfo { ModuleName = "SharedModule", DatabaseName = "DbA", Provider = "SqlServer" }]
        });

        var incoming = new HostTopologyInfo
        {
            HostName = "HostB",
            Modules = [new ModuleDeploymentInfo { ModuleName = "SharedModule", DatabaseName = "DbB", Provider = "SqlServer" }]
        };

        var result = await service.ValidateAsync(incoming);

        result.Should().NotBeNull();
        result.Issues.Should().NotBeEmpty();
    }
}
