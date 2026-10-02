using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Maps the aggregated host health endpoint, unless the application already owns its route.
/// </summary>
/// <remarks>
///     <para>
///         The endpoint is on by default. Mapping a route is an opinion about the host's route table,
///         and an application that already owns <c>/health</c> would collide — but making it opt-in to
///         avoid that gives a health endpoint no application turns on, a bridge built and never
///         crossed, which is the worse of the two failures. So the collision is handled rather than
///         avoided.
///     </para>
///     <para>
///         Handled by looking: the route table is already populated at this point in the generated
///         entry point, so an application that mapped the same path keeps it and this one steps aside
///         with a warning naming the way to silence it. Two endpoints on one route is otherwise an
///         <c>AmbiguousMatchException</c> thrown on the first request — at the moment a probe asks,
///         which is the worst possible time to learn about a configuration mistake.
///     </para>
/// </remarks>
public static class HealthEndpointMapper
{
    /// <summary>
    ///     Maps the health check endpoint at the configured path when nothing else claims it.
    /// </summary>
    /// <param name="app">The application, used both as route table and as the source of the logger.</param>
    /// <param name="options">The host health options — the path, and whether to map at all.</param>
    public static void Map(WebApplication app, HostHealthOptions options)
    {
        Ensure.Ensure.ThrowIfNull(app);
        Ensure.Ensure.ThrowIfNull(options);

        if (!options.Enabled) return;

        var logger = app.Services.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory
            ? factory.CreateLogger("Pragmatic.Composition.Hosting.HealthEndpointMapper")
            : null;

        if (IsRouteTaken(app, options.Path))
        {
            logger?.LogInformation(
                "Health endpoint not mapped: '{Path}' is already mapped by this application. "
                + "The aggregated host health is not published. Call UseHealthEndpoint(\"<other path>\") "
                + "to publish it elsewhere, or DisableHealthEndpoint() to state that this is intended.",
                options.Path);
            return;
        }

        app.MapHealthChecks(options.Path)
            // A liveness probe carries no tenant header, and with RequireTenant — the default — the
            // request would be refused with 400 before reaching the check. Measured: 400 without the
            // header, 200 with it, on a host that had simply enabled this endpoint.
            .WithMetadata(TenantAgnosticEndpoint.Instance);
    }

    /// <summary>Whether any already-mapped endpoint answers on this exact path.</summary>
    /// <remarks>
    ///     Compared on the raw pattern rather than by matching a request: this runs once at startup,
    ///     the path is a literal, and building a matcher to ask one question would cost more than the
    ///     question is worth. A route that differs only by a constraint or a parameter is not the same
    ///     route, and treating it as one would refuse to map over a pattern that never collides.
    /// </remarks>
    private static bool IsRouteTaken(IEndpointRouteBuilder app, string path)
    {
        var normalized = path.TrimStart('/');

        foreach (var dataSource in app.DataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is RouteEndpoint route
                    && string.Equals(route.RoutePattern.RawText?.TrimStart('/'), normalized,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
