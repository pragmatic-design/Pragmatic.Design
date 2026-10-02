using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Composition;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Extension methods for configuring runtime maintenance mode via <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderMaintenanceExtensions
{
    /// <summary>
    ///     Enables runtime maintenance mode with admin panel, SSE progress streaming,
    ///     and database migration tracking.
    /// </summary>
    public static IPragmaticBuilder UseMaintenanceMode(
        this IPragmaticBuilder builder,
        Action<MaintenanceModeOptions>? configure = null)
    {
        // Use the built-in builder's own options instance when available, else a fresh one for a
        // custom IPragmaticBuilder. The `configure` callback is applied to WHICHEVER instance we end
        // up using, so a custom builder does not silently drop the caller's configuration — only the
        // PragmaticBuilder-specific EnableRuntimeMaintenance flag is gated on the concrete type.
        var options = builder is PragmaticBuilder pb
            ? pb.Options.MaintenanceMode
            : new MaintenanceModeOptions();
        if (builder is PragmaticBuilder)
            options.EnableRuntimeMaintenance = true;
        configure?.Invoke(options);

        var progressStream = new MigrationProgressStream();

        // Register the concrete service via a FACTORY (not a bare instance) so the logger is injected
        // and every DI-registered IMaintenanceModeObserver is attached — a bare instance would leave the
        // observer seam unreachable and the logger null. IMaintenanceMode delegates to the
        // same singleton, so resolving either key returns the same object.
        builder.Services.AddSingleton(sp =>
        {
            var service = new MaintenanceModeService(sp.GetService<ILogger<MaintenanceModeService>>());
            foreach (var observer in sp.GetServices<IMaintenanceModeObserver>())
                service.AddObserver(observer);
            return service;
        });
        builder.Services.AddSingleton<IMaintenanceMode>(sp => sp.GetRequiredService<MaintenanceModeService>());
        builder.Services.AddSingleton<IMigrationProgressStream>(progressStream);
        builder.Services.AddSingleton(progressStream);
        builder.Services.AddSingleton(options);

        return builder;
    }
}
