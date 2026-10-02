using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     <see cref="BootstrapLogger"/> gives you immediate logging during early startup —
///     before the DI container (and the real <see cref="ILoggerFactory"/>) exists. Once the
///     container is built you call <see cref="BootstrapLogger.TransitionToFullLogging"/> and
///     every subsequent <c>CreateLogger</c> call transparently routes to the full pipeline.
/// </summary>
public static class BootstrapLoggerSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Bootstrap logger (pre-DI startup) ---");

        // Phase 1: no DI yet. The bootstrap factory writes straight to the console
        // with a minimal, immediate configuration so startup failures are never lost.
        BootstrapLogger.LogApplicationStartup(
            applicationName: "Pragmatic.Logging.Samples",
            version: "1.0.0",
            environment: "Development");

        var earlyLogger = BootstrapLogger.CreateLogger("Startup.Configuration");
        earlyLogger.LogInformation("Reading configuration before the container is built");
        earlyLogger.LogWarning("Optional setting 'Cache:Ttl' missing — falling back to default");

        // Phase 2: the DI container is now ready with the full Pragmatic pipeline.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global => { },
            builder => builder.AddConsole());

        using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        // Hand the real factory to the bootstrap logger. Cached bootstrap loggers are
        // dropped and all future CreateLogger calls use the full pipeline.
        BootstrapLogger.TransitionToFullLogging(loggerFactory);

        // Same API, now backed by the full logging system.
        var appLogger = BootstrapLogger.CreateLogger("Application.Runtime");
        appLogger.LogInformation("Application fully started — bootstrap handed off to DI pipeline");

        BootstrapLogger.LogApplicationShutdown("Pragmatic.Logging.Samples");
    }
}
