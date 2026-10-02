using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Pragmatic.Gateway;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The real gateway <c>Program</c>, configured with the routes the example publishes.
/// </summary>
/// <remarks>
///     The type argument is a public type of the gateway's assembly and not <c>Program</c>, for the reason
///     the hosts have markers: with several executables referenced, <c>Program</c> names the wrong one.
///     The gateway itself needs no real port — the suite is its only caller — while its backends do.
/// </remarks>
internal sealed class GatewayHost(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<GatewayOptions>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }
}
