namespace Pragmatic.Gateway.Middleware;

/// <summary>
///     Extracts tenant from the request (authenticated claim, then subdomain) and sets the
///     X-Tenant-Id header for downstream backends. A client-supplied X-Tenant-Id is always
///     stripped before resolution — trusting an attacker-controllable header would let a
///     user in tenant A query data in tenant B simply by setting the header.
/// </summary>
internal sealed class TenantRoutingMiddleware(
    RequestDelegate next, GatewayOptions options, ILogger<TenantRoutingMiddleware> logger)
{
    private const string TenantHeader = "X-Tenant-Id";
    private const string TenantClaim = "tenant_id";

    /// <summary>
    ///     Infrastructure subdomains that must never be treated as tenant identifiers.
    ///     Extend this set when adding new infrastructure endpoints (e.g. status, admin).
    /// </summary>
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www",
        "api",
        "admin",
        "status",
        "health",
        "metrics",
        "mail",
        "smtp",
        "ftp",
        "cdn",
        "static",
        "assets"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        // Always remove any client-supplied X-Tenant-Id header FIRST. The gateway is the
        // authoritative source of this header; backends downstream must be able to trust it.
        context.Request.Headers.Remove(TenantHeader);

        var tenantId = ResolveTenant(context, options.TrustHostForTenant);

        if (tenantId is not null)
        {
            // Re-add the resolved (gateway-computed) tenant header for forwarding.
            context.Request.Headers[TenantHeader] = tenantId;

            using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
            {
                await next(context).ConfigureAwait(false);
            }

            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static string? ResolveTenant(HttpContext context, bool trustHost)
    {
        // 1. Authenticated tenant claim. This is the only source that cannot be spoofed by the
        //    client, because the JWT is signed by the authority configured on the gateway.
        var claim = context.User?.FindFirst(TenantClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(claim))
            return claim;

        // 2. Subdomain (acme.myapp.com → acme) — OPT-IN only. The HTTP Host header is
        //    client-controllable unless the deployment enforces host filtering, so trusting it to
        //    pick a tenant would let a request without a tenant claim spoof any tenant by setting
        //    Host: victim.myapp.com. Enabled only via GatewayOptions.TrustHostForTenant, which the
        //    operator sets when a proxy / host filtering guarantees the Host cannot be forged.
        if (trustHost)
        {
            var host = context.Request.Host.Host;
            var dotIndex = host.IndexOf('.');
            if (dotIndex > 0)
            {
                var subdomain = host[..dotIndex];
                if (!ReservedSubdomains.Contains(subdomain))
                    return subdomain;
            }
        }

        // The path is not read: there is no route convention that names the tenant in it, so a segment
        // could not be told apart from any other. A deployment that needs it resolves the tenant in the
        // service, where the route is known.
        return null;
    }
}
