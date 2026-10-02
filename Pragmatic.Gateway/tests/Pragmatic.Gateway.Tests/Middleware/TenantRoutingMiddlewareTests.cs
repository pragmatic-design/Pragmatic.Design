using System.Security.Claims;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Gateway.Middleware;
using Xunit;

namespace Pragmatic.Gateway.Tests.Middleware;

/// <summary>
///     Unit tests for <see cref="TenantRoutingMiddleware" /> tenant resolution and header hygiene,
///     exercised over an in-memory <see cref="DefaultHttpContext" />.
/// </summary>
public sealed class TenantRoutingMiddlewareTests
{
    private const string TenantHeader = "X-Tenant-Id";

    private static (TenantRoutingMiddleware Middleware, Func<HttpContext> Captured) Create(bool trustHost = false)
    {
        HttpContext? seen = null;
        var middleware = new TenantRoutingMiddleware(
            ctx =>
            {
                seen = ctx;
                return Task.CompletedTask;
            },
            new GatewayOptions { TrustHostForTenant = trustHost },
            NullLogger<TenantRoutingMiddleware>.Instance);
        return (middleware, () => seen!);
    }

    private static DefaultHttpContext ContextWithHost(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        return context;
    }

    private static ClaimsPrincipal PrincipalWithTenant(string tenantId) =>
        new(new ClaimsIdentity([new Claim("tenant_id", tenantId)], "test"));

    [Fact]
    public async Task InvokeAsync_AuthenticatedTenantClaim_SetsHeaderFromClaim()
    {
        var (middleware, captured) = Create();
        var context = ContextWithHost("acme.example.com");
        context.User = PrincipalWithTenant("claim-tenant");

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("claim-tenant");
    }

    [Fact]
    public async Task InvokeAsync_ClaimTakesPrecedenceOverSubdomain()
    {
        var (middleware, captured) = Create();
        var context = ContextWithHost("subdomain-tenant.example.com");
        context.User = PrincipalWithTenant("claim-tenant");

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("claim-tenant");
    }

    [Fact]
    public async Task InvokeAsync_NoClaimWithSubdomain_ResolvesFromSubdomain()
    {
        var (middleware, captured) = Create(trustHost: true);
        var context = ContextWithHost("acme.example.com");

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_SubdomainNotTrustedByDefault_DoesNotResolveTenant()
    {
        // GW-H4 regression: the Host header is client-spoofable, so subdomain-based tenant
        // resolution is OFF unless TrustHostForTenant is explicitly enabled. Without a signed claim
        // the tenant stays unresolved (and any client-supplied header is stripped).
        var (middleware, captured) = Create(); // trustHost defaults to false
        var context = ContextWithHost("victim.example.com");

        await middleware.InvokeAsync(context);

        captured().Request.Headers.ContainsKey(TenantHeader).Should().BeFalse();
    }

    [Theory]
    [InlineData("www.example.com")]
    [InlineData("api.example.com")]
    [InlineData("admin.example.com")]
    [InlineData("health.example.com")]
    [InlineData("cdn.example.com")]
    public async Task InvokeAsync_ReservedSubdomain_DoesNotSetTenantHeader(string host)
    {
        var (middleware, captured) = Create(trustHost: true);
        var context = ContextWithHost(host);

        await middleware.InvokeAsync(context);

        captured().Request.Headers.ContainsKey(TenantHeader).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_HostWithoutDot_DoesNotSetTenantHeader()
    {
        // No dot in host → no subdomain segment → tenant unresolved.
        var (middleware, captured) = Create();
        var context = ContextWithHost("localhost");

        await middleware.InvokeAsync(context);

        captured().Request.Headers.ContainsKey(TenantHeader).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ClientSuppliedTenantHeader_IsStrippedWhenUnresolvable()
    {
        var (middleware, captured) = Create();
        var context = ContextWithHost("localhost");
        context.Request.Headers[TenantHeader] = "attacker-controlled";

        await middleware.InvokeAsync(context);

        captured().Request.Headers.ContainsKey(TenantHeader).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ClientSuppliedTenantHeader_IsOverwrittenByResolvedTenant()
    {
        var (middleware, captured) = Create(trustHost: true);
        var context = ContextWithHost("acme.example.com");
        context.Request.Headers[TenantHeader] = "spoofed";

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_ClientSuppliedHeaderStripped_ClaimWins()
    {
        var (middleware, captured) = Create();
        var context = ContextWithHost("example.com");
        context.User = PrincipalWithTenant("real-tenant");
        context.Request.Headers[TenantHeader] = "spoofed";

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("real-tenant");
    }

    [Fact]
    public async Task InvokeAsync_WhitespaceClaim_FallsBackToSubdomain()
    {
        var (middleware, captured) = Create(trustHost: true);
        var context = ContextWithHost("acme.example.com");
        context.User = PrincipalWithTenant("   ");

        await middleware.InvokeAsync(context);

        captured().Request.Headers[TenantHeader].ToString().Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        var (middleware, captured) = Create();
        var context = ContextWithHost("example.com");

        await middleware.InvokeAsync(context);

        captured().Should().BeSameAs(context);
    }
}
