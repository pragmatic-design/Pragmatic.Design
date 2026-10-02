using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Pragmatic.Gateway.Tests.Routing;

/// <summary>
///     <c>GatewayOptions.EnableTelemetry</c> (default true) takes effect: the host boots
///     OpenTelemetry tracing + metrics for the gateway and its proxied calls. Booting the real
///     <c>Program</c> and resolving the providers proves the registration is live.
/// </summary>
public sealed class TelemetryWiringTests(GatewayAppFactory factory) : IClassFixture<GatewayAppFactory>
{
    [Fact]
    public void EnableTelemetry_Default_RegistersOpenTelemetryProviders()
    {
        factory.Services.GetService<TracerProvider>().Should().NotBeNull();
        factory.Services.GetService<MeterProvider>().Should().NotBeNull();
    }
}
