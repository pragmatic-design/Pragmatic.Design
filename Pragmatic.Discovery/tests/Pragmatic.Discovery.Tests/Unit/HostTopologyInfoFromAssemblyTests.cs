using System.Reflection;
using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Tests the reflection-based <see cref="HostTopologyInfo.FromAssembly"/> path.
/// This path inspects <c>[assembly: PragmaticMetadata]</c> attributes and is
/// independent of the global metadata registry.
/// </summary>
public class HostTopologyInfoFromAssemblyTests
{
    [Fact]
    public void FromAssembly_WithAssemblyWithoutHostTopologyAttribute_ReturnsNull()
    {
        // The test assembly carries no HostTopology PragmaticMetadata attribute.
        var assembly = typeof(HostTopologyInfoFromAssemblyTests).Assembly;

        var result = HostTopologyInfo.FromAssembly(assembly);

        result.Should().BeNull();
    }

    [Fact]
    public void FromAssembly_WithMscorlibAssembly_ReturnsNull()
    {
        // A framework assembly likewise has no Pragmatic metadata attribute.
        var assembly = typeof(object).Assembly;

        var result = HostTopologyInfo.FromAssembly(assembly);

        result.Should().BeNull();
    }

    [Fact]
    public void FromEntryAssembly_DoesNotThrow_AndReturnsParsedOrNull()
    {
        // FromEntryAssembly resolves via registry first, then assembly reflection.
        // Either outcome is valid for a non-host test process; it must never throw.
        var act = () => HostTopologyInfo.FromEntryAssembly();

        act.Should().NotThrow();
    }
}
