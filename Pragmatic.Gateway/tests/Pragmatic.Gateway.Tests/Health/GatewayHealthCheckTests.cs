using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pragmatic.Agent.Client;
using Pragmatic.Gateway.Health;
using Xunit;

namespace Pragmatic.Gateway.Tests.Health;

/// <summary>
///     Unit tests for <see cref="GatewayHealthCheck" />. A freshly constructed
///     <see cref="AgentConnection" /> is never connected, so the check reports Degraded.
/// </summary>
public sealed class GatewayHealthCheckTests
{
    private static GatewayHealthCheck Create() =>
        new(new AgentConnection("test-agent-not-connected"));

    private static HealthCheckContext Context() => new();

    [Fact]
    public async Task CheckHealthAsync_AgentDisconnected_ReturnsDegraded()
    {
        var check = Create();

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_AgentDisconnected_ReportsAgentNotConnectedInData()
    {
        var check = Create();

        var result = await check.CheckHealthAsync(Context());

        result.Data.Should().ContainKey("agent_connected");
        result.Data["agent_connected"].Should().Be(false);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesTimestampInData()
    {
        var check = Create();

        var result = await check.CheckHealthAsync(Context());

        result.Data.Should().ContainKey("timestamp");
        result.Data["timestamp"].Should().BeOfType<string>();
    }

    [Fact]
    public async Task CheckHealthAsync_DegradedResult_HasDescription()
    {
        var check = Create();

        var result = await check.CheckHealthAsync(Context());

        result.Description.Should().Contain("Agent disconnected");
    }
}
