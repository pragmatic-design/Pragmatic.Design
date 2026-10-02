using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

public class InMemoryDiscoveryBackendTests
{
    private readonly InMemoryDiscoveryBackend _backend = new();

    private static HostTopologyInfo CreateTopology(string hostName, params string[] moduleNames)
    {
        return new HostTopologyInfo
        {
            HostName = hostName,
            Modules = moduleNames.Select(m => new ModuleDeploymentInfo { ModuleName = m }).ToList()
        };
    }

    [Fact]
    public async Task StoreAsync_AndGetByHostName_ReturnsStoredTopology()
    {
        var topology = CreateTopology("HostA", "ModuleA");

        await _backend.StoreAsync(topology);

        var result = await _backend.GetByHostNameAsync("HostA");
        result.Should().NotBeNull();
        result!.HostName.Should().Be("HostA");
        result.Modules.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByHostNameAsync_WithNonExistentHost_ReturnsNull()
    {
        var result = await _backend.GetByHostNameAsync("NonExistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByHostNameAsync_IsCaseInsensitive()
    {
        var topology = CreateTopology("MyHost", "ModuleA");
        await _backend.StoreAsync(topology);

        var upper = await _backend.GetByHostNameAsync("MYHOST");
        var lower = await _backend.GetByHostNameAsync("myhost");
        var mixed = await _backend.GetByHostNameAsync("MyHost");

        upper.Should().NotBeNull();
        lower.Should().NotBeNull();
        mixed.Should().NotBeNull();
    }

    [Fact]
    public async Task StoreAsync_WithSameHostName_OverwritesPrevious()
    {
        var original = CreateTopology("HostA", "ModuleA");
        var updated = CreateTopology("HostA", "ModuleB", "ModuleC");

        await _backend.StoreAsync(original);
        await _backend.StoreAsync(updated);

        var result = await _backend.GetByHostNameAsync("HostA");
        result.Should().NotBeNull();
        result!.Modules.Should().HaveCount(2);
        result.Modules[0].ModuleName.Should().Be("ModuleB");
    }

    [Fact]
    public async Task GetAllAsync_WithNoEntries_ReturnsEmptyList()
    {
        var result = await _backend.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllStoredTopologies()
    {
        await _backend.StoreAsync(CreateTopology("HostA", "ModuleA"));
        await _backend.StoreAsync(CreateTopology("HostB", "ModuleB"));
        await _backend.StoreAsync(CreateTopology("HostC", "ModuleC"));

        var result = await _backend.GetAllAsync();

        result.Should().HaveCount(3);
        result.Select(h => h.HostName).Should().BeEquivalentTo("HostA", "HostB", "HostC");
    }

    [Fact]
    public async Task StoreAsync_WithCaseVariantHostName_OverwritesCaseInsensitively()
    {
        await _backend.StoreAsync(CreateTopology("HostA", "ModuleA"));
        await _backend.StoreAsync(CreateTopology("hosta", "ModuleB"));

        var all = await _backend.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].Modules[0].ModuleName.Should().Be("ModuleB");
    }
}
