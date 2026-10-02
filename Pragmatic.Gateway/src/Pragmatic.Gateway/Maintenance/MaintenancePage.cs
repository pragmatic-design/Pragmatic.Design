using System.Net;

namespace Pragmatic.Gateway.Maintenance;

/// <summary>
///     Writes the 503 maintenance response. Shared by the early full-gateway <see cref="MaintenanceMiddleware"/>
///     and the per-app check in the reverse-proxy pipeline (GW-M3), so both serve an identical page and the
///     optional custom page is loaded/cached in one place.
/// </summary>
internal sealed class MaintenancePage(GatewayOptions options)
{
    private static readonly byte[] DefaultHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Maintenance</title>
            <style>
                body { font-family: system-ui, sans-serif; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; background: #f8f9fa; color: #343a40; }
                .container { text-align: center; padding: 2rem; }
                h1 { font-size: 2rem; margin-bottom: 0.5rem; }
                p { color: #6c757d; font-size: 1.1rem; }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>Under Maintenance</h1>
                <p>We're updating the system. Please try again in a few minutes.</p>
            </div>
        </body>
        </html>
        """u8.ToArray();

    // Cached on first successful load. Volatile so the write from one thread is visible to all readers.
    private volatile byte[]? _customPage;

    /// <summary>Writes a 503 with Retry-After and the (custom or default) maintenance page body.</summary>
    public async Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "60";
        context.Response.ContentType = "text/html; charset=utf-8";

        var page = _customPage ?? TryLoadCustomPage() ?? DefaultHtml;
        await context.Response.Body.WriteAsync(page).ConfigureAwait(false);
    }

    private byte[]? TryLoadCustomPage()
    {
        if (string.IsNullOrEmpty(options.MaintenancePagePath))
            return null;

        try
        {
            if (!File.Exists(options.MaintenancePagePath))
                return null;

            var bytes = File.ReadAllBytes(options.MaintenancePagePath);
            _customPage = bytes;
            return bytes;
        }
        catch (Exception ex)
        {
            AgentLoggerBridge.Warn($"Failed to load custom maintenance page from '{options.MaintenancePagePath}': {ex.Message}");
            return null;
        }
    }
}

/// <summary>Minimal logging shim so the page loader can warn without an injected logger.</summary>
file static class AgentLoggerBridge
{
    public static void Warn(string message) => System.Diagnostics.Trace.TraceWarning(message);
}
