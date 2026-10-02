using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Context provider that extracts information from ASP.NET Core HttpContext.
/// </summary>
public sealed class HttpContextProvider(
    IHttpContextAccessor httpContextAccessor,
    HttpContextEnrichmentOptions? options = null)
    : ContextProviderBase("HttpContext", priority: 100)
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new Dictionary<string, object?>(0);

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    private readonly HttpContextEnrichmentOptions _options = options ?? new HttpContextEnrichmentOptions();

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
            return EmptyProperties;

        var request = httpContext.Request;
        var response = httpContext.Response;
        var user = httpContext.User;
        var connection = httpContext.Connection;

        // Pre-sized to avoid intermediate List + ToArray allocations on every request.
        // Only non-null values are added (matching CreatePropertiesDictionary semantics).
        var properties = new Dictionary<string, object?>(24, StringComparer.Ordinal);

        Add(properties, "RequestId", httpContext.TraceIdentifier);
        Add(properties, "RequestPath", request.Path.Value);
        Add(properties, "RequestMethod", request.Method);
        Add(properties, "RequestScheme", request.Scheme);
        Add(properties, "RequestHost", request.Host.Value);
        Add(properties, "RequestQueryString", request.QueryString.Value);
        Add(properties, "RequestContentType", request.ContentType);
        Add(properties, "RequestContentLength", request.ContentLength);

        Add(properties, "ResponseStatusCode", response.HasStarted ? (int?)response.StatusCode : null);
        Add(properties, "ResponseContentType", response.ContentType);

        // PII / GDPR: only include the client IP when explicitly enabled, masked by default.
        if (_options.IncludeRemoteIpAddress && connection.RemoteIpAddress is { } remoteIp)
        {
            Add(properties, "RemoteIpAddress",
                _options.MaskClientIpAddress ? MaskIpAddress(remoteIp) : remoteIp.ToString());
        }

        Add(properties, "RemotePort", connection.RemotePort);
        Add(properties, "LocalIpAddress", connection.LocalIpAddress?.ToString());
        Add(properties, "LocalPort", connection.LocalPort);

        Add(properties, "IsAuthenticated", user.Identity?.IsAuthenticated);
        Add(properties, "UserName", user.Identity?.Name);
        Add(properties, "AuthenticationType", user.Identity?.AuthenticationType);

        // Add user claims if authenticated
        if (user.Identity?.IsAuthenticated == true)
        {
            Add(properties, "UserId", user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Add(properties, "UserRole", user.FindFirst(ClaimTypes.Role)?.Value);

            // PII: email is only emitted when explicitly opted in.
            if (_options.IncludeUserEmail)
                Add(properties, "UserEmail", user.FindFirst(ClaimTypes.Email)?.Value);
        }

        // Add custom headers as context (be careful with sensitive data)
        AddCustomHeaders(properties, request);

        return properties;
    }

    private static void Add(Dictionary<string, object?> target, string name, object? value)
    {
        if (value != null)
            target[name] = value;
    }

    // Headers we surface as context. Static so it is allocated once, not per request.
    private static readonly string[] InterestingHeaders =
    {
        "User-Agent",
        "X-Forwarded-For",
        "X-Real-IP",
        "X-Correlation-ID",
        "X-Request-ID",
        "Accept-Language",
        "Referer"
    };

    private static void AddCustomHeaders(Dictionary<string, object?> target, HttpRequest request)
    {
        foreach (var headerName in InterestingHeaders)
        {
            if (request.Headers.TryGetValue(headerName, out var values) && values.Count > 0)
            {
                target[$"Header_{headerName}"] = values.ToString();
            }
        }
    }

    /// <summary>
    /// Masks an IP address for privacy: zeroes the last octet of IPv4 and the low
    /// bits of IPv6, preserving coarse network locality without identifying the host.
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
            for (var i = 6; i < bytes.Length; i++)
                bytes[i] = 0;
        }

        return new System.Net.IPAddress(bytes).ToString();
    }
}