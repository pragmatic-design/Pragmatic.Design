using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Demonstrates tenant resolution and header forwarding the Gateway performs before proxying.
///     <para>
///         The production <c>TenantRoutingMiddleware</c> is internal. This sample reproduces its exact
///         logic against a <see cref="DefaultHttpContext" />: it always strips a client-supplied
///         <c>X-Tenant-Id</c> (anti-spoofing), then resolves the tenant from (1) the signed JWT
///         <c>tenant_id</c> claim, (2) the host subdomain (excluding reserved infra subdomains), and
///         re-adds a gateway-computed <c>X-Tenant-Id</c> for the downstream backend.
///     </para>
/// </summary>
internal static class TenantRoutingSample
{
    private const string TenantHeader = "X-Tenant-Id";
    private const string TenantClaim = "tenant_id";

    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "status", "health", "metrics", "mail", "smtp", "ftp", "cdn", "static", "assets"
    };

    public static void Run()
    {
        SampleConsole.Header("TenantRoutingMiddleware — resolution + header forwarding");

        SampleConsole.Note("X-Tenant-Id from the client is ALWAYS stripped first — trusting it would allow cross-tenant access.");

        // 1. Signed JWT claim wins (cannot be spoofed; the gateway validated the signature).
        Demo(
            "JWT claim 'tenant_id' present",
            BuildContext(host: "app.example.com", tenantClaim: "acme-corp", spoofedHeader: "evil-tenant"));

        // 2. Subdomain fallback when no claim.
        Demo(
            "Subdomain fallback (no claim)",
            BuildContext(host: "globex.example.com", tenantClaim: null, spoofedHeader: null));

        // 3. Reserved infra subdomain is NOT treated as a tenant.
        Demo(
            "Reserved subdomain 'api' ignored",
            BuildContext(host: "api.example.com", tenantClaim: null, spoofedHeader: null));

        // 4. Apex domain (no subdomain) → unresolved.
        Demo(
            "Apex domain, no claim → unresolved",
            BuildContext(host: "example.com", tenantClaim: null, spoofedHeader: "spoof"));
    }

    private static void Demo(string title, HttpContext context)
    {
        SampleConsole.Section(title);

        var clientSupplied = context.Request.Headers[TenantHeader].ToString();
        SampleConsole.Item("client X-Tenant-Id", string.IsNullOrEmpty(clientSupplied) ? "(none)" : clientSupplied);

        var forwarded = ResolveAndForward(context);

        SampleConsole.Item("host", context.Request.Host.Host);
        SampleConsole.Item("forwarded X-Tenant-Id", forwarded ?? "(none — not forwarded)");
    }

    /// <summary>Mirrors TenantRoutingMiddleware: strip client header, resolve, re-add gateway value.</summary>
    private static string? ResolveAndForward(HttpContext context)
    {
        context.Request.Headers.Remove(TenantHeader); // anti-spoofing: gateway is authoritative

        var tenantId = ResolveTenant(context);
        if (tenantId is not null)
            context.Request.Headers[TenantHeader] = tenantId;

        return tenantId;
    }

    private static string? ResolveTenant(HttpContext context)
    {
        var claim = context.User?.FindFirst(TenantClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(claim))
            return claim;

        var host = context.Request.Host.Host;
        var dotIndex = host.IndexOf('.');
        if (dotIndex > 0)
        {
            var subdomain = host[..dotIndex];
            if (!ReservedSubdomains.Contains(subdomain))
                return subdomain;
        }

        return null;
    }

    private static DefaultHttpContext BuildContext(string host, string? tenantClaim, string? spoofedHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);

        if (spoofedHeader is not null)
            context.Request.Headers[TenantHeader] = spoofedHeader;

        if (tenantClaim is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantClaim, tenantClaim)], "jwt"));

        return context;
    }
}
