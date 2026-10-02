using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pragmatic.Telemetry;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Configures OpenTelemetry tracing, metrics, and logging for Pragmatic applications.
///     Registers all known Pragmatic ActivitySources and Meters automatically.
/// </summary>
public static class PragmaticTelemetry
{
    // All known Pragmatic module source names (safe to register even if module is not used)
    private static readonly string[] PragmaticSourceNames =
    [
        "Pragmatic.Actions",
        "Pragmatic.Persistence",
        "Pragmatic.Events",
        "Pragmatic.Caching",
        "Pragmatic.Resilience",
        "Pragmatic.Internationalization",
        "Pragmatic.Validation",
        "Pragmatic.Logging",
        "Pragmatic.Authorization",
        "Pragmatic.Jobs",
        "Pragmatic.Messaging",
        "Pragmatic.Email",
        "Pragmatic.Notifications",
        "Pragmatic.Migrations",
    ];

    /// <summary>
    ///     Adds OpenTelemetry to the service collection with Pragmatic sensible defaults.
    ///     All Pragmatic ActivitySources and Meters are registered automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">
    ///     Telemetry configuration options. Passed as a plain value (not <c>IOptions&lt;TelemetryOptions&gt;</c>)
    ///     BY DESIGN: this method runs at registration / build time, before the DI container exists, so
    ///     there is nothing to resolve an <c>IOptions&lt;T&gt;</c> from — the caller supplies the already-resolved
    ///     options. This matches the ASP.NET Core <c>AddXxx(TOptions)</c> startup-builder convention, not the
    ///     runtime <c>IOptions&lt;T&gt;</c> consumption pattern.
    /// </param>
    /// <param name="isDevelopment">Whether the host is running in development mode.</param>
    /// <param name="serviceName">Override for the service name. Falls back to options.ServiceName or assembly name.</param>
    /// <param name="additionalSources">
    ///     Optional extra ActivitySource / Meter names (third-party modules or user extensions) to register
    ///     alongside the built-in Pragmatic sources. Null/blank entries are ignored.
    /// </param>
    public static IServiceCollection AddPragmaticTelemetry(
        this IServiceCollection services,
        TelemetryOptions options,
        bool isDevelopment = false,
        string? serviceName = null,
        IEnumerable<string>? additionalSources = null)
    {
        if (!options.Enabled)
            return services;

        // Built-in Pragmatic sources + any third-party/user ActivitySource & Meter names the caller
        // wants registered through the same pipeline. Distinct so a duplicate is harmless.
        var allSources = additionalSources is null
            ? (IReadOnlyList<string>)PragmaticSourceNames
            : [.. PragmaticSourceNames, .. additionalSources.Where(s => !string.IsNullOrWhiteSpace(s))];

        var resolvedServiceName = serviceName
            ?? options.ServiceName
            ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
            ?? "pragmatic-app";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(resolvedServiceName);

        var otel = services.AddOpenTelemetry();

        if (options.Tracing)
        {
            otel.WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);

                // Register all Pragmatic ActivitySources
                foreach (var source in allSources)
                    tracing.AddSource(source);

                // ASP.NET Core, HttpClient, and EF Core instrumentation
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                tracing.AddEntityFrameworkCoreInstrumentation();

                // Sampling: AlwaysOn in dev, ratio-based in prod
                if (!isDevelopment && options.SamplingRatio < 1.0)
                {
                    tracing.SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(options.SamplingRatio)));
                }

                ConfigureExporter(tracing, options, isDevelopment);
            });
        }

        if (options.Metrics)
        {
            otel.WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);

                // Register all Pragmatic Meters
                foreach (var source in allSources)
                    metrics.AddMeter(source);

                // ASP.NET Core metrics
                metrics.AddAspNetCoreInstrumentation();

                ConfigureMetricExporter(metrics, options, isDevelopment);
            });
        }

        if (options.Logging)
        {
            otel.WithLogging(logging =>
            {
                logging.SetResourceBuilder(resourceBuilder);
                ConfigureLogExporter(logging, options, isDevelopment);
            });

            // Configure OTel logger options for richer log records
            services.Configure<OpenTelemetryLoggerOptions>(logOptions =>
            {
                logOptions.IncludeFormattedMessage = true;
                logOptions.IncludeScopes = true;
            });
        }

        return services;
    }

    private static void ConfigureExporter(TracerProviderBuilder tracing, TelemetryOptions options, bool isDevelopment)
    {
        if (options.UseOtlpExporter)
        {
            tracing.AddOtlpExporter();
        }
        else if (isDevelopment)
        {
            tracing.AddConsoleExporter();
        }
    }

    private static void ConfigureMetricExporter(MeterProviderBuilder metrics, TelemetryOptions options, bool isDevelopment)
    {
        if (options.UseOtlpExporter)
        {
            metrics.AddOtlpExporter();
        }
        else if (isDevelopment)
        {
            metrics.AddConsoleExporter();
        }
    }

    private static void ConfigureLogExporter(LoggerProviderBuilder logging, TelemetryOptions options, bool isDevelopment)
    {
        if (options.UseOtlpExporter)
        {
            logging.AddOtlpExporter();
        }
        else if (isDevelopment)
        {
            logging.AddConsoleExporter();
        }
    }
}
