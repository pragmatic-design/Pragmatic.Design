using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.AspNetCore.Detection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Temporal.AspNetCore.Middleware;

/// <summary>
///     Middleware that creates a <see cref="TemporalContext" /> for each request.
///     Detects client timezone using configured strategies.
/// </summary>
public sealed partial class TemporalContextMiddleware
{
    private static readonly ActivitySource ActivitySource = new("Pragmatic.Temporal", "1.0.0");

    private readonly ILogger<TemporalContextMiddleware>? _logger;
    private readonly RequestDelegate _next;

    /// <summary>Creates a new instance.</summary>
    public TemporalContextMiddleware(RequestDelegate next, ILogger<TemporalContextMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        using var activity = ActivitySource.StartActivity("temporal.context.detection");

        var options = context.RequestServices.GetRequiredService<IOptions<TemporalOptions>>().Value;
        var aspOptions = context.RequestServices.GetService<IOptions<TemporalAspNetCoreOptions>>()?.Value
                         ?? new TemporalAspNetCoreOptions();
        var clock = context.RequestServices.GetRequiredService<IClock>();

        // Detect client timezone using strategies
        var clientZone = options.DefaultTimeZone;
        string? detectionStrategy = null;

        foreach (var strategy in aspOptions.DetectionStrategies.OrderBy(s => s.Priority))
        {
            var detected = strategy.Detect(context);
            if (detected != null)
            {
                clientZone = detected;
                detectionStrategy = strategy.GetType().Name;
                if (_logger is not null)
                    LogTimezoneDetected(_logger, TimeZoneResolver.GetIanaId(clientZone), detectionStrategy);
                break;
            }
        }

        if (detectionStrategy is null)
        {
            // No strategy resolved a zone. Distinguish "no timezone supplied" from
            // "supplied but invalid" so ThrowOnInvalidTimeZone can be honored.
            var invalidValue = FindInvalidSuppliedTimeZone(context, aspOptions);
            if (invalidValue is not null)
            {
                if (options.ThrowOnInvalidTimeZone)
                    throw new TimeZoneNotFoundException(
                        $"Client supplied an invalid timezone '{invalidValue}' and " +
                        $"{nameof(TemporalOptions.ThrowOnInvalidTimeZone)} is enabled.");

                if (_logger is not null)
                    LogInvalidTimezoneFallback(_logger, invalidValue,
                        TimeZoneResolver.GetIanaId(options.DefaultTimeZone));
            }
            else if (_logger is not null)
            {
                LogDefaultTimezoneUsed(_logger, TimeZoneResolver.GetIanaId(options.DefaultTimeZone));
            }
        }

        // Set activity tags for distributed tracing
        activity?.SetTag(TemporalTags.ClientTimeZone, TimeZoneResolver.GetIanaId(clientZone));
        activity?.SetTag(TemporalTags.BusinessTimeZone, TimeZoneResolver.GetIanaId(options.BusinessTimeZone));
        activity?.SetTag(TemporalTags.DetectionStrategy, detectionStrategy ?? "default");

        // Create temporal context, flowing module-level policy defaults through.
        var temporalContext = new TemporalContext
        {
            Clock = clock,
            ClientTimeZone = clientZone,
            BusinessTimeZone = options.BusinessTimeZone,
            NonExistentTimeHandling = options.NonExistentTimeHandling,
            AmbiguousTimeHandling = options.AmbiguousTimeHandling,
            FirstDayOfWeek = options.FirstDayOfWeek,
            DefaultCountryCode = options.DefaultCountryCode
        };

        // Store in HttpContext.Items for this request, and expose it ambiently so JSON
        // serialization (configured once per app) can honor per-request timezones.
        context.Items[typeof(TemporalContext)] = temporalContext;
        TemporalContextHolder.Current = temporalContext;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            TemporalContextHolder.Current = null;
        }
    }

    /// <summary>
    ///     Returns the first raw timezone value a client actually supplied (in strategy
    ///     priority order) that failed to resolve to a valid zone, or null if the client
    ///     supplied nothing. Only strategies that opt into <see cref="IRawTimeZoneDetectionStrategy" />
    ///     participate; legacy strategies cannot distinguish absent from invalid.
    /// </summary>
    private static string? FindInvalidSuppliedTimeZone(HttpContext context, TemporalAspNetCoreOptions aspOptions)
    {
        foreach (var strategy in aspOptions.DetectionStrategies.OrderBy(s => s.Priority))
        {
            if (strategy is not IRawTimeZoneDetectionStrategy raw)
                continue;

            var rawValue = raw.GetRawValue(context);
            if (!string.IsNullOrWhiteSpace(rawValue) && !TimeZoneResolver.IsValidTimezone(rawValue))
                return SanitizeForLog(rawValue);
        }

        return null;
    }

    /// <summary>
    ///     Bounds and cleans a client-supplied value before it reaches logs or exception
    ///     messages: truncated to 64 chars (the longest valid zone id is shorter) and
    ///     control characters replaced, so a hostile header cannot forge log lines.
    /// </summary>
    private static string SanitizeForLog(string value)
    {
        const int maxLength = 64;
        var truncated = value.Length <= maxLength ? value : value[..maxLength];

        return string.Create(truncated.Length, truncated, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) ? '?' : c;
            }
        });
    }

    // LoggerMessage source-generated methods (zero allocation)

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detected client timezone '{Timezone}' using {Strategy}")]
    private static partial void LogTimezoneDetected(ILogger logger, string timezone, string strategy);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No timezone detected, using default: {DefaultTimezone}")]
    private static partial void LogDefaultTimezoneUsed(ILogger logger, string defaultTimezone);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Client supplied invalid timezone '{InvalidTimezone}', falling back to default: {DefaultTimezone}")]
    private static partial void LogInvalidTimezoneFallback(ILogger logger, string invalidTimezone, string defaultTimezone);
}