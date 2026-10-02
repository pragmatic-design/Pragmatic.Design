using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Agent.Discovery;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     It replaces the discovery backend rather than adding one. Leaving the default registered
///     alongside would mean the resolved backend depends on registration order — which is the kind of
///     thing that behaves in a test host and picks the other one in production.
/// </remarks>
public class PragmaticBuilderAgentDiscoveryExtensionsTests
{
    [Fact]
    public void UseAgentDiscovery_ReplacesTheDiscoveryBackend()
    {
        // A stand-in for whatever backend was registered first: what matters is that it does not
        // survive alongside the agent one, not which one it was.
        var services = new ServiceCollection();
        services.AddSingleton<IDiscoveryBackend>(_ => null!);

        services.UseAgentDiscovery();

        services.Count(d => d.ServiceType == typeof(IDiscoveryBackend)).Should().Be(1,
            "it replaces the backend, so exactly one must be left standing");
    }
}
