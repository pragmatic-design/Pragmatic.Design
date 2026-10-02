using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     Boots the real gateway <c>Program</c> under a test server for BUG-G1 wiring assertions.
///     <c>Gateway:AgentSocketPath</c> is forced empty so the startup <c>AgentConnection.ConnectAsync</c>
///     fails fast (an empty pipe/socket name throws synchronously) instead of blocking on a missing
///     daemon; the gateway then serves the static routes supplied here from configuration.
/// </summary>
public sealed class GatewayAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Gateway:AgentSocketPath", string.Empty);
        builder.UseSetting("Gateway:Routes:0:RouteId", "api");
        builder.UseSetting("Gateway:Routes:0:Path", "/api/{**catch-all}");
        builder.UseSetting("Gateway:Routes:0:Backends:0", "http://backend.local:5000");
        builder.UseSetting("Gateway:Routes:0:Host", "gateway.local");
    }
}
