using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// The global options and the presets of <see cref="PragmaticLoggingBuilder" />.
/// </summary>
/// <remarks>
///     One builder, not two: two public classes with one name and different <c>Add*</c> overloads make
///     which one a reader gets depend on a <c>using</c>. Each preset writes
///     <see cref="PragmaticLoggingOptions" />.
/// </remarks>
public sealed partial class PragmaticLoggingBuilder
{
    /// <summary>
    /// Configures the global Pragmatic.Logging options.
    /// </summary>
    /// <param name="configure">Configuration action for the global options</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder Configure(Action<PragmaticLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Applies a throughput preset: background processing, larger buffers, Information and above.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder UseHighPerformancePreset()
        => Configure(options =>
        {
            options.HighPerformanceMode = true;
            options.Performance.EnableZeroAllocation = true;
            options.Performance.UseBackgroundProcessing = true;
            options.Performance.BufferSize = 10000;
            options.Performance.FlushThreshold = 1000;
            options.MinimumLevel = LogLevel.Information;
        });

    /// <summary>
    /// Applies a development preset: every level, request and machine context, no telemetry.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder UseDevelopmentPreset()
        => Configure(options =>
        {
            options.HighPerformanceMode = false;
            options.MinimumLevel = LogLevel.Trace;
            options.EnableTelemetry = false;
            options.Context.IncludeUserContext = false;
            options.Context.IncludeRequestContext = true;
            options.Context.IncludeMachineContext = true;
        });

    /// <summary>
    /// Applies a production preset: background processing, correlation and user context, rate limiting.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder UseProductionPreset()
        => Configure(options =>
        {
            options.HighPerformanceMode = false;
            options.MinimumLevel = LogLevel.Information;
            options.EnableTelemetry = true;
            options.Performance.UseBackgroundProcessing = true;
            options.Performance.BufferSize = 5000;
            options.Context.IncludeCorrelationId = true;
            options.Context.IncludeUserContext = true;
            options.RateLimiting.Enabled = true;
            options.RateLimiting.MaxMessagesPerSecond = 1000;
        });

    /// <summary>
    /// Applies a compliance preset for regulatory requirements: redaction, secret detection and a
    /// file-system audit trail.
    /// </summary>
    /// <param name="standard">The compliance standard to configure for</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder UseCompliancePreset(ComplianceStandard standard = ComplianceStandard.Gdpr)
        => Configure(options =>
        {
            options.Privacy.EnableRedaction = true;
            options.Privacy.EnableSecretDetection = true;
            options.Privacy.ComplianceStandard = standard.ToString();
            options.Audit.Enabled = true;
            // Durable, on-disk audit trail. Database-backed storage is not implemented and would
            // throw at startup; an application that needs it registers its own IAuditStorage.
            options.Audit.StorageType = "FileSystem";

            switch (standard)
            {
                case ComplianceStandard.Gdpr:
                    options.Privacy.DataRedaction.PreserveLengths = false; // complete redaction
                    options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.8;
                    break;

                case ComplianceStandard.Hipaa:
                    options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.9;
                    options.Privacy.DataRedaction.PreserveLengths = false;
                    break;

                case ComplianceStandard.PciDss:
                    options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.95;
                    break;
            }
        });

    /// <summary>
    /// Enables and configures data redaction.
    /// </summary>
    /// <param name="configure">Configuration action for data redaction options</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder EnableDataRedaction(Action<DataRedactionConfigOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return Configure(options =>
        {
            options.Privacy.EnableRedaction = true;
            configure(options.Privacy.DataRedaction);
        });
    }

    /// <summary>
    /// Enables and configures the audit trail.
    /// </summary>
    /// <param name="configure">Configuration action for audit options</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder EnableAuditTrail(Action<AuditOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return Configure(options =>
        {
            options.Audit.Enabled = true;
            configure(options.Audit);
        });
    }

    /// <summary>
    /// Enables and configures rate limiting.
    /// </summary>
    /// <param name="configure">Configuration action for rate limiting options</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder EnableRateLimiting(Action<RateLimitingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return Configure(options =>
        {
            options.RateLimiting.Enabled = true;
            configure(options.RateLimiting);
        });
    }
}
