using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Middleware that automatically enriches logging context for HTTP requests.
/// </summary>
public sealed class LoggingEnrichmentMiddleware(
    RequestDelegate next,
    ILogger<LoggingEnrichmentMiddleware> logger,
    LoggingEnrichmentOptions? options = null)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly ILogger<LoggingEnrichmentMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly LoggingEnrichmentOptions _options = options ?? new LoggingEnrichmentOptions();

    public async Task InvokeAsync(HttpContext context)
    {
        // Start timing the request
        var stopwatch = Stopwatch.StartNew();

        // Create correlation ID early
        var correlationId = CorrelationIdProvider.GetOrCreateCorrelationId(context);

        // Create enriched logging context
        using var logContext = CreateRequestLogContext(context, correlationId);

        try
        {
            // Log request start if enabled
            if (_options.LogRequestStart)
            {
                LogRequestStart(context, correlationId);
            }

            // Execute the next middleware
            await _next(context);
        }
        catch (Exception ex)
        {
            // Log unhandled exceptions
            if (_options.LogUnhandledExceptions)
            {
                LogUnhandledException(context, ex, correlationId, stopwatch.ElapsedMilliseconds);
            }
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Log request completion if enabled
            if (_options.LogRequestCompletion)
            {
                LogRequestCompletion(context, correlationId, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private IDisposable CreateRequestLogContext(HttpContext context, string correlationId)
    {
        var contextProperties = new List<KeyValuePair<string, object?>>
        {
            new("CorrelationId", correlationId),
            new("RequestPath", context.Request.Path.Value),
            new("RequestMethod", context.Request.Method),
            new("RequestId", context.TraceIdentifier)
        };

        // Add user information if authenticated
        if (context.User.Identity?.IsAuthenticated == true)
        {
            contextProperties.Add(new("UserId", context.User.Identity.Name));
        }

        // Add custom properties from options
        if (_options.ContextEnricher != null)
        {
            var customProperties = _options.ContextEnricher(context);
            contextProperties.AddRange(customProperties);
        }

        return LogContextScope.PushContext(contextProperties);
    }

    private void LogRequestStart(HttpContext context, string correlationId)
    {
        var request = context.Request;

        var scope = new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RequestMethod"] = request.Method,
            ["RequestPath"] = request.Path.Value,
            ["RequestQueryString"] = request.QueryString.Value,
            ["RequestHost"] = request.Host.Value,
            ["UserAgent"] = request.Headers.TryGetValue("User-Agent", out var userAgent) ? userAgent.ToString() : null
        };

        // PII / GDPR: only emit the client IP when explicitly enabled, masked by default.
        if (_options.IncludeRemoteIpAddress && context.Connection.RemoteIpAddress is { } remoteIp)
        {
            scope["RemoteIpAddress"] = _options.MaskClientIpAddress
                ? MaskIpAddress(remoteIp)
                : remoteIp.ToString();
        }

        using (_logger.BeginScope(scope))
        {
            _logger.LogInformation("HTTP request started: {RequestMethod} {RequestPath}",
                request.Method, request.Path.Value);
        }
    }

    private void LogRequestCompletion(HttpContext context, string correlationId, long elapsedMs)
    {
        var request = context.Request;
        var response = context.Response;

        var logLevel = GetLogLevelForStatusCode(response.StatusCode);

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RequestMethod"] = request.Method,
            ["RequestPath"] = request.Path.Value,
            ["StatusCode"] = response.StatusCode,
            ["ElapsedMilliseconds"] = elapsedMs,
            ["ContentLength"] = response.ContentLength,
            ["ContentType"] = response.ContentType
        }))
        {
            _logger.Log(logLevel,
                "HTTP request completed: {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMs}ms",
                request.Method, request.Path.Value, response.StatusCode, elapsedMs);
        }
    }

    private void LogUnhandledException(HttpContext context, Exception exception, string correlationId, long elapsedMs)
    {
        var request = context.Request;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RequestMethod"] = request.Method,
            ["RequestPath"] = request.Path.Value,
            ["ElapsedMilliseconds"] = elapsedMs,
            ["ExceptionType"] = exception.GetType().Name
        }))
        {
            _logger.LogError(exception,
                "HTTP request failed: {RequestMethod} {RequestPath} threw {ExceptionType} after {ElapsedMs}ms",
                request.Method, request.Path.Value, exception.GetType().Name, elapsedMs);
        }
    }

    /// <summary>
    /// Masks an IP address for privacy: zeroes the last octet of IPv4 and the low
    /// 80 bits of IPv6, preserving coarse network locality without identifying the host.
    /// </summary>
    private static string MaskIpAddress(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            bytes[3] = 0;
        }
        else
        {
            // IPv6: keep the /48 prefix, zero the rest.
            for (var i = 6; i < bytes.Length; i++)
                bytes[i] = 0;
        }

        return new System.Net.IPAddress(bytes).ToString();
    }

    private static LogLevel GetLogLevelForStatusCode(int statusCode)
    {
        return statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };
    }
}

/// <summary>
/// Options for configuring the LoggingEnrichmentMiddleware.
/// </summary>
public sealed class LoggingEnrichmentOptions
{
    /// <summary>
    /// Gets or sets whether to log request start events.
    /// </summary>
    public bool LogRequestStart { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to log request completion events.
    /// </summary>
    public bool LogRequestCompletion { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to log unhandled exceptions.
    /// </summary>
    public bool LogUnhandledExceptions { get; set; } = true;

    /// <summary>
    /// Gets or sets a custom context enricher function.
    /// </summary>
    public Func<HttpContext, IEnumerable<KeyValuePair<string, object?>>>? ContextEnricher { get; set; }

    /// <summary>
    /// Gets or sets whether the client's remote IP address is included in logs.
    /// PII / GDPR: disabled by default. When enabled, consider also enabling
    /// <see cref="MaskClientIpAddress"/> to avoid persisting the full address.
    /// </summary>
    public bool IncludeRemoteIpAddress { get; set; }

    /// <summary>
    /// Gets or sets whether an included remote IP address is masked before logging
    /// (last octet of IPv4 / low 80 bits of IPv6 zeroed). Defaults to <c>true</c>.
    /// Only relevant when <see cref="IncludeRemoteIpAddress"/> is enabled.
    /// </summary>
    public bool MaskClientIpAddress { get; set; } = true;
}