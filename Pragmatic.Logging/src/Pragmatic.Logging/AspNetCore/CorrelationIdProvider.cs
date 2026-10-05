using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Context provider that manages correlation IDs for request tracking.
/// Supports W3C Trace Context: when a <c>traceparent</c> header is present,
/// uses the TraceId as correlation ID for unified log-trace correlation.
/// </summary>
public sealed class CorrelationIdProvider(IHttpContextAccessor httpContextAccessor, string? headerName = null)
    : ContextProviderBase("CorrelationId", priority: 50)
{
    public const string CorrelationIdHeaderName = "X-Correlation-ID";
    public const string TraceparentHeaderName = "traceparent";
    public const string CorrelationIdKey = "CorrelationId";
    public const string TraceIdKey = "TraceId";
    public const string SpanIdKey = "SpanId";

    /// <summary>
    /// Maximum accepted length of an inbound correlation id. Values longer than this are
    /// rejected and a fresh id is generated to avoid unbounded/abusive header values.
    /// </summary>
    public const int MaxCorrelationIdLength = 128;

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <summary>
    /// The header name used to read and write the correlation id. Defaults to
    /// <see cref="CorrelationIdHeaderName"/> but can be overridden via configuration.
    /// </summary>
    public string HeaderName { get; } = string.IsNullOrWhiteSpace(headerName) ? CorrelationIdHeaderName : headerName;

    /// <inheritdoc />
    /// <remarks>Not static: the correlation id belongs to the request being served when the entry is written.</remarks>
    public override bool IsStatic => false;

    /// <inheritdoc />
    public override bool IsAvailable()
    {
        return _httpContextAccessor.HttpContext != null;
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return new Dictionary<string, object?>();

        var correlationId = GetOrCreateCorrelationId(httpContext, HeaderName);

        // Include W3C trace context if Activity.Current is available
        var activity = Activity.Current;
        if (activity is not null)
        {
            return CreatePropertiesDictionary(
                (CorrelationIdKey, correlationId),
                (TraceIdKey, activity.TraceId.ToString()),
                (SpanIdKey, activity.SpanId.ToString())
            );
        }

        return CreatePropertiesDictionary(
            (CorrelationIdKey, correlationId)
        );
    }

    /// <summary>
    /// Gets the correlation ID from the current HTTP context, or creates one if none exists.
    /// </summary>
    /// <param name="httpContext">The HTTP context</param>
    /// <returns>The correlation ID</returns>
    public static string GetOrCreateCorrelationId(HttpContext httpContext)
        => GetOrCreateCorrelationId(httpContext, CorrelationIdHeaderName);

    /// <summary>
    /// Gets the correlation ID from the current HTTP context, or creates one if none exists,
    /// using the supplied header name for both reading the inbound value and writing the response.
    /// </summary>
    /// <param name="httpContext">The HTTP context</param>
    /// <param name="headerName">The header name to read/write the correlation id</param>
    /// <returns>The correlation ID</returns>
    public static string GetOrCreateCorrelationId(HttpContext httpContext, string headerName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (string.IsNullOrWhiteSpace(headerName))
            headerName = CorrelationIdHeaderName;

        // Check if we already have a correlation ID in the context
        if (httpContext.Items.TryGetValue(CorrelationIdKey, out var existingId) && existingId is string correlationId)
        {
            return correlationId;
        }

        // Try to get correlation ID from request headers.
        // SECURITY: the inbound value is untrusted and is echoed back in the response header.
        // Without validation an attacker could inject CR/LF or oversized values
        // (header-injection / response-splitting). Only accept a short, safe-charset value;
        // otherwise fall through and generate a fresh id.
        if (httpContext.Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            if (IsValidCorrelationId(headerValue))
            {
                httpContext.Items[CorrelationIdKey] = headerValue!;
                if (!httpContext.Response.HasStarted)
                    httpContext.Response.Headers[headerName] = headerValue;
                return headerValue!;
            }
        }

        // W3C Trace Context: use TraceId from traceparent header or Activity.Current
        // This ensures correlation ID aligns with distributed tracing
        var activity = Activity.Current;
        if (activity is not null)
        {
            var traceId = activity.TraceId.ToString();
            httpContext.Items[CorrelationIdKey] = traceId;
            if (!httpContext.Response.HasStarted)
                httpContext.Response.Headers[headerName] = traceId;
            return traceId;
        }

        // Generate a new correlation ID
        var newCorrelationId = Guid.NewGuid().ToString("D");
        httpContext.Items[CorrelationIdKey] = newCorrelationId;

        // Add to response headers for client tracking
        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers[headerName] = newCorrelationId;
        }

        return newCorrelationId;
    }

    /// <summary>
    /// Validates an inbound correlation id before it is trusted and echoed to the response.
    /// Accepts only a non-empty, bounded-length value consisting of ASCII alphanumerics,
    /// '-', '_' and '.' — a charset that cannot carry header-injection sequences.
    /// </summary>
    /// <param name="value">The candidate correlation id (may be null).</param>
    /// <returns><c>true</c> when safe to use as-is; otherwise <c>false</c>.</returns>
    public static bool IsValidCorrelationId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxCorrelationIdLength)
            return false;

        foreach (var c in value)
        {
            var isSafe = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                         or '-' or '_' or '.';
            if (!isSafe)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the correlation ID from the current HTTP context without creating one.
    /// </summary>
    /// <param name="httpContext">The HTTP context</param>
    /// <returns>The correlation ID if it exists, null otherwise</returns>
    public static string? GetCorrelationId(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Items.TryGetValue(CorrelationIdKey, out var existingId) && existingId is string correlationId)
        {
            return correlationId;
        }

        return null;
    }

    /// <summary>
    /// Sets a correlation ID in the current HTTP context.
    /// </summary>
    /// <param name="httpContext">The HTTP context</param>
    /// <param name="correlationId">The correlation ID to set</param>
    public static void SetCorrelationId(HttpContext httpContext, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        httpContext.Items[CorrelationIdKey] = correlationId;

        // Add to response headers for client tracking
        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers[CorrelationIdHeaderName] = correlationId;
        }
    }
}