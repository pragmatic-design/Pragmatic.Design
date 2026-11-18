using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Pragmatic.Persistence.EFCore.Extensions;

/// <summary>
///     EF Core's OpenTelemetry instrumentation, registered by the assembly that brings EF Core.
/// </summary>
/// <remarks>
///     <para>
///         It used to be registered by <c>Pragmatic.Composition.Host</c>, which made every host carry it, an engines-only host too. That
///         host has no EF Core command to trace, and published Native AOT the instrumentation alone raised four IL2026 warnings:
///         <c>EntityFrameworkDiagnosticListener</c> reads properties by reflection, which its package says cannot be made trim compatible
///         (#858). The generated host now calls this only when it references <c>Pragmatic.Persistence.EFCore</c>.
///     </para>
///     <para>
///         <c>ConfigureOpenTelemetryTracerProvider</c> creates no tracer provider: the instrumentation joins the one the host's telemetry
///         builds, and does nothing where telemetry or tracing is turned off.
///     </para>
/// </remarks>
public static class PragmaticEntityFrameworkCoreTelemetryExtensions
{
    /// <summary>Adds EF Core command tracing to the application's OpenTelemetry tracer provider, when it has one.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddPragmaticEntityFrameworkCoreTracing(this IServiceCollection services)
    {
        Ensure.Ensure.ThrowIfNull(services);
        return services.ConfigureOpenTelemetryTracerProvider(static tracing => tracing.AddEntityFrameworkCoreInstrumentation());
    }
}
