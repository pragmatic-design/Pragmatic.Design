using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Registers the maintenance 503 middleware, and — only when
///     <see cref="MaintenanceModeOptions.EnableAdminEndpoints" /> is set — the admin endpoints.
///     Order -100 ensures this runs before all other steps (routing, CORS, auth, etc.).
/// </summary>
/// <remarks>
///     Registered by the generated host, unconditionally. The middleware is the only thing that turns
///     an active <see cref="IMaintenanceMode" /> into an actual 503: without it a control-plane
///     EnterMaintenanceCommand sets the flag and traffic keeps flowing.
/// </remarks>
public sealed class MaintenanceStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => -100;

    /// <inheritdoc />
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // MaintenanceModeService and MigrationProgressStream are registered
        // by UseMaintenanceMode() or auto-registered by the SG entry template
    }

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseMiddleware<MaintenanceMiddleware>();

        // The admin panel, its SSE feed and the restart verb add routes to the host's public
        // surface, so they are opt-in — unlike the middleware above, which is always wired.
        if (app is not WebApplication webApp)
            return;

        var options = app.ApplicationServices.GetService<MaintenanceModeOptions>()
                      ?? new MaintenanceModeOptions();

        if (!options.EnableAdminEndpoints)
            return;

        MaintenanceAdminEndpoints.Map(webApp, options);
    }
}
