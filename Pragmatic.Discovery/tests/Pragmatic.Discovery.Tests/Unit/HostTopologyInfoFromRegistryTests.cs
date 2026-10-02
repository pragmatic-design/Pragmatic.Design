using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Tests the zero-reflection <see cref="HostTopologyInfo.FromRegistry"/> path against
/// the process-global metadata registry, seeded by <see cref="HostTopologyRegistryFixture"/>.
/// </summary>
[Collection(HostTopologyRegistryCollection.Name)]
public class HostTopologyInfoFromRegistryTests
{
    [Fact]
    public void FromRegistry_WithRegisteredHostTopology_ReturnsParsedTopology()
    {
        var result = HostTopologyInfo.FromRegistry();

        result.Should().NotBeNull();
        result!.HostName.Should().Be(HostTopologyRegistryFixture.HostName);
    }

    [Fact]
    public void FromRegistry_ReturnsTopologyWithModulesFromJson()
    {
        var result = HostTopologyInfo.FromRegistry();

        result.Should().NotBeNull();
        result!.Modules.Should().ContainSingle();
        result.Modules[0].ModuleName.Should().Be("FixtureModule");
        result.Modules[0].DatabaseName.Should().Be("FixtureDb");
        result.Modules[0].Provider.Should().Be("InMemory");
    }

    [Fact]
    public void FromEntryAssembly_WithRegisteredHostTopology_PrefersRegistryResult()
    {
        // FromEntryAssembly prefers the registry over assembly reflection;
        // with the fixture provider registered it must return the registry topology.
        var result = HostTopologyInfo.FromEntryAssembly();

        result.Should().NotBeNull();
        result!.HostName.Should().Be(HostTopologyRegistryFixture.HostName);
    }
}
