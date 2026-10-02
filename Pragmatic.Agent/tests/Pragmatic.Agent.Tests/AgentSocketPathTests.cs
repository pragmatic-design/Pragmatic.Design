using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Xunit;

namespace Pragmatic.Agent.Tests;

/// <summary>
///     The shared socket-path convention used by both the daemon and clients (Gateway, app host) — the
///     single source that lets <c>GatewayOptions.AgentInstance</c> resolve the same socket the daemon binds.
/// </summary>
public class AgentSocketPathTests
{
    [Fact]
    public void Resolve_NullEmptyAndDefault_AreEquivalent()
    {
        var expected = AgentSocketPath.Resolve("default");
        AgentSocketPath.Resolve(null).Should().Be(expected);
        AgentSocketPath.Resolve("").Should().Be(expected);
        AgentSocketPath.Resolve("   ").Should().Be(expected);
    }

    [Fact]
    public void Resolve_NamedInstance_IsDistinctAndCarriesTheName()
    {
        var def = AgentSocketPath.Resolve("default");
        var named = AgentSocketPath.Resolve("billing");

        named.Should().NotBe(def);
        named.Should().Contain("billing");
    }

    [Fact]
    public void Resolve_SameInstance_IsStable()
    {
        AgentSocketPath.Resolve("billing").Should().Be(AgentSocketPath.Resolve("billing"));
    }
}
