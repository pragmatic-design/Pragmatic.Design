using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Returns 503 Service Unavailable when maintenance mode is active.
///     Admin paths are always allowed through. Zero overhead when inactive (volatile bool check).
/// </summary>
public sealed class MaintenanceMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly IMaintenanceMode _maintenanceMode;
    private readonly string _adminPath;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next delegate in the request pipeline.</param>
    /// <param name="maintenanceMode">The maintenance mode state to check on each request.</param>
    /// <param name="options">Maintenance options supplying the admin path that is always allowed through.</param>
    public MaintenanceMiddleware(
        RequestDelegate next,
        IMaintenanceMode maintenanceMode,
        MaintenanceModeOptions options)
    {
        _next = next;
        _maintenanceMode = maintenanceMode;
        _adminPath = options.AdminPath.TrimEnd('/');
    }

    /// <summary>
    ///     Short-circuits the request with <c>503 Service Unavailable</c> while maintenance mode is active,
    ///     except for admin paths which are always forwarded. Passes through with near-zero overhead when inactive.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Fast path — not in maintenance
        if (!_maintenanceMode.IsActive)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Admin panel is always accessible during maintenance. Match on a SEGMENT boundary —
        // a bare StartsWith("/_admin") would also let "/_administrator-bypass" through. ASP.NET
        // Core has already URL-decoded and normalized PathString (%2F → /, collapsed slashes),
        // so a raw StartsWith on Path.Value is safe against encoding tricks; the only gap is the
        // missing segment boundary, which the exact-or-prefix-with-slash check below closes.
        var path = context.Request.Path.Value ?? string.Empty;
        var isAdminPath = path.Equals(_adminPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(_adminPath + "/", StringComparison.OrdinalIgnoreCase);
        if (isAdminPath)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Return 503 with maintenance info
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";

        if (_maintenanceMode.EstimatedEnd.HasValue)
        {
            // Clamp to [1, int.MaxValue] BEFORE casting: a double > int.MaxValue (EstimatedEnd ~68+
            // years out) would otherwise wrap to a negative Retry-After. Math.Min on doubles first,
            // then cast, is overflow-safe.
            var retrySeconds = (_maintenanceMode.EstimatedEnd.Value - DateTimeOffset.UtcNow).TotalSeconds;
            var retryAfter = (int)Math.Clamp(retrySeconds, 1, int.MaxValue);
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
        }

        var response = new MaintenanceUnavailableResponse(
            "Service Unavailable",
            "Application is in maintenance mode",
            _maintenanceMode.Reason,
            _maintenanceMode.ActivatedAt,
            _maintenanceMode.EstimatedEnd);

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(
                response, MaintenanceAdminJsonContext.Default.MaintenanceUnavailableResponse))
            .ConfigureAwait(false);
    }
}
