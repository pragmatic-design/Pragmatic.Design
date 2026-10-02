using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Demonstrates the maintenance response the Gateway serves while a backend is down: a 503 with a
///     <c>Retry-After</c> header and an HTML body, optionally loaded from
///     <see cref="GatewayOptions.MaintenancePagePath" />.
///     <para>
///         The production <c>MaintenanceMiddleware</c> + <c>MaintenanceState</c> are internal and react
///         to Agent KV transitions (<c>state/gateway/maintenance</c>, <c>state/app:*</c>). This sample
///         reproduces the exact response-writing path against a <see cref="DefaultHttpContext" />, so
///         the observable behaviour (status, headers, body) is runnable without an Agent.
///     </para>
/// </summary>
internal static class MaintenanceSample
{
    private static readonly byte[] DefaultMaintenanceHtml =
        Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><h1>Under Maintenance</h1></body></html>");

    public static async Task RunAsync()
    {
        SampleConsole.Header("Maintenance — 503 + Retry-After + custom page");

        // ── State transitions the Gateway tracks from Agent KV ───────────────────────────────
        SampleConsole.Section("Agent KV → maintenance state (mapping the gateway applies)");
        SampleConsole.Item("state/gateway/maintenance=true", "full gateway maintenance — ALL requests 503");
        SampleConsole.Item("state/app:billing=maintenance", "app in maintenance (also: 'migrating', 'draining')");
        SampleConsole.Item("state/app:billing=running", "app removed from maintenance set");

        // ── Fast path: not in maintenance → request flows through ────────────────────────────
        SampleConsole.Section("Not in maintenance (fast path)");
        var passthrough = new DefaultHttpContext();
        var forwarded = await HandleAsync(passthrough, inMaintenance: false, options: new GatewayOptions());
        SampleConsole.Item("forwarded to backend?", forwarded);
        SampleConsole.Item("status code", passthrough.Response.StatusCode);

        // ── In maintenance, built-in default page ────────────────────────────────────────────
        SampleConsole.Section("In maintenance — built-in default page");
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await HandleAsync(ctx, inMaintenance: true, options: new GatewayOptions());
        await DumpResponse(ctx);

        // ── In maintenance, custom page from disk ────────────────────────────────────────────
        SampleConsole.Section("In maintenance — custom page from MaintenancePagePath");
        var customPath = Path.Combine(Path.GetTempPath(), $"pragmatic-maint-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(customPath,
            "<!DOCTYPE html><html><body><h1>Be right back ✦</h1><p>Custom branded page.</p></body></html>");
        try
        {
            var customCtx = new DefaultHttpContext();
            customCtx.Response.Body = new MemoryStream();
            await HandleAsync(customCtx, inMaintenance: true, options: new GatewayOptions { MaintenancePagePath = customPath });
            await DumpResponse(customCtx);
        }
        finally
        {
            File.Delete(customPath);
        }
    }

    /// <summary>
    ///     Mirrors MaintenanceMiddleware.InvokeAsync / ServeMaintenancePageAsync. Returns true if the
    ///     request was forwarded (not in maintenance), false if a 503 page was served.
    /// </summary>
    private static async Task<bool> HandleAsync(HttpContext context, bool inMaintenance, GatewayOptions options)
    {
        if (!inMaintenance)
            return true; // would call next(context)

        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "60";
        context.Response.ContentType = "text/html; charset=utf-8";

        var page = TryLoadCustomPage(options) ?? DefaultMaintenanceHtml;
        await context.Response.Body.WriteAsync(page);
        return false;
    }

    private static byte[]? TryLoadCustomPage(GatewayOptions options)
    {
        if (string.IsNullOrEmpty(options.MaintenancePagePath) || !File.Exists(options.MaintenancePagePath))
            return null;
        try { return File.ReadAllBytes(options.MaintenancePagePath); }
        catch { return null; } // gateway logs + falls back to default
    }

    private static async Task DumpResponse(HttpContext ctx)
    {
        SampleConsole.Item("status code", ctx.Response.StatusCode);
        SampleConsole.Item("Retry-After", ctx.Response.Headers["Retry-After"].ToString());
        SampleConsole.Item("Content-Type", ctx.Response.ContentType);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        SampleConsole.Item("body length", $"{body.Length} bytes");
        SampleConsole.Item("body preview", body.Length <= 80 ? body : body[..80] + "...");
    }
}
