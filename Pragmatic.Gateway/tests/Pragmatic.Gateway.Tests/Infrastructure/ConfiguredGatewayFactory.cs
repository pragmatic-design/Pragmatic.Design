using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pragmatic.Gateway.Tests.Infrastructure;

/// <summary>
///     Boots the real gateway <c>Program</c> with the settings a test gives it.
/// </summary>
/// <remarks>
///     Settings go through <c>UseSetting</c> because <c>Program.cs</c> binds <c>GatewayOptions</c> while it
///     registers the services, before configuration added later would be visible. The Agent socket is
///     forced empty for the reason <see cref="Routing.GatewayAppFactory" /> gives: the connection fails
///     fast and the gateway serves the static routes configured here.
/// </remarks>
internal sealed class ConfiguredGatewayFactory(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Gateway:AgentSocketPath", string.Empty);

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }
}
