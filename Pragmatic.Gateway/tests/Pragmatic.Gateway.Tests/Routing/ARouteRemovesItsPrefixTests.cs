using Pragmatic.Gateway.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     The gateway publishes a service under a prefix and forwards the rest, so the service does
///     not have to know where it is published.
/// </summary>
/// <remarks>
///     Asserted on the path the backend <b>received</b>, which is the only place the transform is visible:
///     a route config that carries the transform and a proxy that ignores it would look identical from the
///     gateway's side.
/// </remarks>
public sealed class ARouteRemovesItsPrefixTests : IAsyncLifetime
{
    private EchoBackend _orders = null!;

    public async Task InitializeAsync() => _orders = await EchoBackend.StartAsync("orders").ConfigureAwait(false);

    public async Task DisposeAsync() => await _orders.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task WithPathRemovePrefix_TheServiceReceivesThePathWithoutIt()
    {
        using var gateway = GatewayFor(pathRemovePrefix: "/orders");
        using var client = gateway.CreateClient();

        var answer = await client.GetStringAsync("/orders/health?probe=1");

        answer.Should().Be("orders /health?probe=1",
            "the prefix is where the gateway publishes the service, and the query string is the caller's");
    }

    /// <summary>The control: a route that says nothing forwards the path as it arrived.</summary>
    [Fact]
    public async Task WithoutPathRemovePrefix_TheServiceReceivesThePathUnchanged()
    {
        using var gateway = GatewayFor(pathRemovePrefix: null);
        using var client = gateway.CreateClient();

        var answer = await client.GetStringAsync("/orders/health");

        answer.Should().Be("orders /orders/health");
    }

    /// <summary>A prefix the route's path does not start with could never strip anything.</summary>
    [Fact]
    public void APrefixThePathDoesNotStartWith_FailsTheStart_NamingTheRoute()
    {
        using var gateway = GatewayFor(pathRemovePrefix: "/shipping");

        var start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*orders*/shipping*");
    }

    private ConfiguredGatewayFactory GatewayFor(string? pathRemovePrefix)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gateway:Routes:0:RouteId"] = "orders",
            ["Gateway:Routes:0:Path"] = "/orders/{**catch-all}",
            ["Gateway:Routes:0:Backends:0"] = _orders.Address,
        };

        if (pathRemovePrefix is not null)
            settings["Gateway:Routes:0:PathRemovePrefix"] = pathRemovePrefix;

        return new ConfiguredGatewayFactory(settings);
    }
}
