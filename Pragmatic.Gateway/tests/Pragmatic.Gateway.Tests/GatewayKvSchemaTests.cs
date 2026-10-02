using Pragmatic.Testing.Assertions;
using Pragmatic.Gateway;
using Xunit;

namespace Pragmatic.Gateway.Tests;

/// <summary>P6: the Gateway↔Agent KV key schema is a single versioned contract, not scattered literals.</summary>
public class GatewayKvSchemaTests
{
    [Fact]
    public void KeyHelpers_ComposeUnderTheDeclaredPrefixes()
    {
        GatewayKvSchema.RouteKey("api").Should().Be("gateway/routes/api");
        GatewayKvSchema.ClusterKey("billing").Should().Be("gateway/clusters/billing");
    }

    /// <summary>The roster prefix is the Agent's, which writes it: one contract, not two literals.</summary>
    [Fact]
    public void HostRoster_IsTheAgentsPerInstanceKey()
    {
        GatewayKvSchema.HostRosterPrefix.Should().Be(Pragmatic.Agent.Protocol.Payloads.HostRosterKeys.Prefix);
        Pragmatic.Agent.Protocol.Payloads.HostRosterKeys.Key("booking", "i-1").Should().Be("state/app:booking/i-1");
    }

    [Fact]
    public void RoutesAndClusters_ShareTheGatewayPrefix()
    {
        GatewayKvSchema.RoutesPrefix.Should().StartWith(GatewayKvSchema.Prefix);
        GatewayKvSchema.ClustersPrefix.Should().StartWith(GatewayKvSchema.Prefix);
    }

    [Fact]
    public void SchemaVersion_IsSet()
    {
        GatewayKvSchema.SchemaVersion.Should().Be("2.0.0");
    }
}
