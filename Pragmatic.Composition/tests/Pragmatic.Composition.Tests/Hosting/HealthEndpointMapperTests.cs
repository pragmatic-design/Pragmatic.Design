using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Hosting;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     Who gets <c>/health</c>, now that the host maps it by default.
/// </summary>
/// <remarks>
///     <para>
///         The endpoint was opt-in, for a reason that was written down and real: mapping a route is an
///         opinion about the host's route table. What the caution produced was a health endpoint
///         <b>no application in this repository ever turned on</b> — the contributors collected on
///         every host, their verdict published on none.
///     </para>
///     <para>
///         Default-on only holds if the collision it was avoiding is handled, so these tests are about
///         the handling: the route is taken, or it is free, or the application said no.
///     </para>
/// </remarks>
public class HealthEndpointMapperTests
{
    private static WebApplication NewApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        builder.Logging.ClearProviders();
        return builder.Build();
    }

    // DataSources is an explicit interface implementation on WebApplication, so it is read through
    // the interface — the same way HealthEndpointMapper reads it.
    private static string[] RoutesOf(WebApplication app)
        => [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText!)];

    [Fact]
    public void OnAFreeRoute_TheEndpointIsMapped()
    {
        var app = NewApp();

        HealthEndpointMapper.Map(app, new HostHealthOptions());

        RoutesOf(app).Should().Contain("/health");
    }

    /// <remarks>
    ///     The collision an opt-in endpoint would avoid. Two endpoints on one route is an
    ///     <c>AmbiguousMatchException</c> thrown on the first request — which, for a health route, is
    ///     the moment a probe asks, and the answer it gets is a 500 that reads like the application is
    ///     broken. Stepping aside keeps the application's own answer, which is the one it meant.
    /// </remarks>
    [Fact]
    public void WhenTheApplicationOwnsTheRoute_TheHostStepsAside()
    {
        var app = NewApp();
        app.MapGet("/health", () => "mine");

        HealthEndpointMapper.Map(app, new HostHealthOptions());

        RoutesOf(app).Count(r => r == "/health").Should().Be(1,
            "the application's endpoint stays, and the host does not add a second one beside it");
    }

    /// <remarks>
    ///     A route that differs by a parameter or a segment is a different route. Treating "similar"
    ///     as "taken" would silently withhold the endpoint from an application that never collided —
    ///     the same silence this change exists to remove, arriving from the other direction.
    /// </remarks>
    [Fact]
    public void ARouteThatMerelyLooksSimilar_DoesNotCount()
    {
        var app = NewApp();
        app.MapGet("/health/{component}", (string component) => component);
        app.MapGet("/api/agent-demo/health", () => "agent");

        HealthEndpointMapper.Map(app, new HostHealthOptions());

        RoutesOf(app).Should().Contain("/health");
    }

    [Fact]
    public void WhenTheApplicationDisabledIt_NothingIsMapped()
    {
        var app = NewApp();

        HealthEndpointMapper.Map(app, new HostHealthOptions { Enabled = false });

        RoutesOf(app).Should().NotContain("/health");
    }

    [Fact]
    public void OnAConfiguredPath_ThatIsWhereItGoes()
    {
        var app = NewApp();

        HealthEndpointMapper.Map(app, new HostHealthOptions { Path = "/internal/alive" });

        RoutesOf(app).Should().Contain("/internal/alive");
        RoutesOf(app).Should().NotContain("/health");
    }

    /// <summary>
    ///     The endpoint declares that it belongs to no tenant.
    /// </summary>
    /// <remarks>
    ///     Without it, a multi-tenant host answers a liveness probe with <b>400</b> — measured on a
    ///     running application: 400 with no <c>X-Tenant-Id</c>, 200 with one. A probe sends no such
    ///     header, so the application would have been marked dead and restarted for ever, by a check
    ///     that never ran.
    /// </remarks>
    [Fact]
    public void TheEndpoint_DeclaresItselfTenantAgnostic()
    {
        var app = NewApp();

        HealthEndpointMapper.Map(app, new HostHealthOptions());

        var health = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/health");

        health.Metadata.GetMetadata<TenantAgnosticEndpoint>().Should().NotBeNull();
    }
}
