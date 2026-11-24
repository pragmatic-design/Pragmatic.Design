using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     A daemon a test starts reads its secret key and its environment name from the variables it inherits. Other tests of this assembly set
///     them in the test host for a moment, to values the daemon refuses at startup: a daemon started in that window exited before it was
///     ready (the gossip tests, under the gate's parallelism). It starts from a defined environment instead.
/// </summary>
[Collection(EnvironmentSensitiveCollection.Name)]
public sealed class ADaemonIgnoresWhatTheTestHostsEnvironmentHoldsTests
{
    private const string GossipKey = "a-daemon-ignores-the-test-hosts-environment-key";

    [Fact]
    public async Task ADaemonStarted_WhileTheHostHoldsAKeyItRefuses_BecomesReady()
    {
        var key = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", "not!base64!!!");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            await using var agent = await AgentProcess.StartAsync(GossipKey);

            agent.GossipPort.Should().BeGreaterThan(0, "the daemon became ready and reported its port");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", key);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
        }
    }
}
