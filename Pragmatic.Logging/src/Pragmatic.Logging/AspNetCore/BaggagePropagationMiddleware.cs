using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
///     Middleware that propagates well-known context values into <see cref="Activity.Baggage"/>.
///     Baggage items are automatically forwarded to downstream services via W3C Baggage headers,
///     enabling cross-service context propagation for tenant ID, user ID, and custom values.
/// </summary>
public sealed class BaggagePropagationMiddleware(RequestDelegate next, BaggagePropagationOptions? options = null)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly BaggagePropagationOptions _options = options ?? new BaggagePropagationOptions();

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            await _next(context);
            return;
        }

        // Propagate user ID from authenticated principal
        if (_options.PropagateUserId && context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(_options.UserIdClaimType)?.Value
                         ?? context.User.Identity.Name;

            if (!string.IsNullOrEmpty(userId))
                activity.SetBaggage("user.id", userId);
        }

        // Propagate tenant ID from header or HttpContext.Items
        if (_options.PropagateTenantId)
        {
            var tenantId = GetTenantId(context);
            if (!string.IsNullOrEmpty(tenantId))
                activity.SetBaggage("tenant.id", tenantId);
        }

        // Propagate custom baggage items from headers
        foreach (var header in _options.CustomBaggageHeaders)
        {
            if (context.Request.Headers.TryGetValue(header.HeaderName, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                    activity.SetBaggage(header.BaggageKey, value);
            }
        }

        // Propagate custom baggage from enricher delegate
        _options.BaggageEnricher?.Invoke(context, activity);

        await _next(context);
    }

    private static string? GetTenantId(HttpContext context)
    {
        // Try X-Tenant-ID header first
        if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValues))
        {
            var value = headerValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        // Try HttpContext.Items (set by TenantResolutionMiddleware)
        if (context.Items.TryGetValue("TenantId", out var tenantObj) && tenantObj is string tenantId)
            return tenantId;

        return null;
    }
}

/// <summary>
///     Options for configuring W3C Baggage propagation.
/// </summary>
public sealed class BaggagePropagationOptions
{
    /// <summary>
    ///     Propagate authenticated user ID into <c>user.id</c> baggage item.
    ///     Default: true.
    /// </summary>
    public bool PropagateUserId { get; set; } = true;

    /// <summary>
    ///     The claim type to extract user ID from. Default: "sub".
    /// </summary>
    public string UserIdClaimType { get; set; } = "sub";

    /// <summary>
    ///     Propagate tenant ID into <c>tenant.id</c> baggage item.
    ///     Default: true.
    /// </summary>
    public bool PropagateTenantId { get; set; } = true;

    /// <summary>
    ///     Custom header-to-baggage mappings.
    /// </summary>
    public List<BaggageHeaderMapping> CustomBaggageHeaders { get; set; } = [];

    /// <summary>
    ///     Optional delegate for custom baggage enrichment.
    /// </summary>
    public Action<HttpContext, Activity>? BaggageEnricher { get; set; }
}

/// <summary>
///     Maps an HTTP header to a W3C Baggage key.
/// </summary>
public sealed class BaggageHeaderMapping
{
    /// <summary>The HTTP header name to read from.</summary>
    public required string HeaderName { get; init; }

    /// <summary>The baggage key to set.</summary>
    public required string BaggageKey { get; init; }
}
