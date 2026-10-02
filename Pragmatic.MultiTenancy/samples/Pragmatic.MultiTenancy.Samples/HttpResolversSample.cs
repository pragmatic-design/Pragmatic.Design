using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Resolvers;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Executes all four HTTP-based <see cref="ITenantResolver"/> implementations against a
///     fabricated <see cref="DefaultHttpContext"/> (no web server needed) and prints the
///     resolved tenant ID for each, including the security edge cases the resolvers guard against.
/// </summary>
public static class HttpResolversSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. HTTP Resolvers — Header / Claim / Subdomain / Route (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await HeaderResolverAsync();
        await ClaimResolverAsync();
        await SubdomainResolverAsync();
        await RouteResolverAsync();
    }

    private static async Task HeaderResolverAsync()
    {
        Console.WriteLine("  5.1 HeaderTenantResolver (X-Tenant-Id)");
        Console.WriteLine("  --------------------------------------");
        var options = Options.Create(new MultiTenancyOptions());

        var withHeader = ContextWith(ctx => ctx.Request.Headers[options.Value.TenantHeaderName] = "  acme  ");
        var resolver = new HeaderTenantResolver(Accessor(withHeader), options);
        Console.WriteLine($"    Header '  acme  '   → \"{await resolver.ResolveAsync()}\" (trimmed)");

        var noHeader = new HeaderTenantResolver(Accessor(ContextWith(_ => { })), options);
        Console.WriteLine($"    No header           → {Fmt(await noHeader.ResolveAsync())}");

        var huge = ContextWith(ctx => ctx.Request.Headers[options.Value.TenantHeaderName] = new string('x', 200));
        var hugeResolver = new HeaderTenantResolver(Accessor(huge), options);
        Console.WriteLine($"    200-char header     → {Fmt(await hugeResolver.ResolveAsync())} (length-capped)");
        Console.WriteLine();
    }

    private static async Task ClaimResolverAsync()
    {
        Console.WriteLine("  5.2 ClaimTenantResolver (tenant_id claim)");
        Console.WriteLine("  -----------------------------------------");
        var options = Options.Create(new MultiTenancyOptions());

        var withClaim = ContextWith(ctx =>
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "tenant-77")], "test")));
        var resolver = new ClaimTenantResolver(Accessor(withClaim), options);
        Console.WriteLine($"    tenant_id=tenant-77 → \"{await resolver.ResolveAsync()}\"");

        var noClaim = new ClaimTenantResolver(Accessor(ContextWith(_ => { })), options);
        Console.WriteLine($"    No claim            → {Fmt(await noClaim.ResolveAsync())}");
        Console.WriteLine();
    }

    private static async Task SubdomainResolverAsync()
    {
        Console.WriteLine("  5.3 SubdomainTenantResolver (acme.app.com → acme)");
        Console.WriteLine("  -------------------------------------------------");

        var sub = ContextWith(ctx => ctx.Request.Host = new HostString("acme.app.com"));
        var resolver = new SubdomainTenantResolver(Accessor(sub));
        Console.WriteLine($"    acme.app.com        → \"{await resolver.ResolveAsync()}\"");

        var ip = ContextWith(ctx => ctx.Request.Host = new HostString("192.168.1.1"));
        var ipResolver = new SubdomainTenantResolver(Accessor(ip));
        Console.WriteLine($"    192.168.1.1         → {Fmt(await ipResolver.ResolveAsync())} (IP rejected)");

        var apex = ContextWith(ctx => ctx.Request.Host = new HostString("app.com"));
        var apexResolver = new SubdomainTenantResolver(Accessor(apex));
        Console.WriteLine($"    app.com (apex)      → {Fmt(await apexResolver.ResolveAsync())} (needs 3 segments)");
        Console.WriteLine();
    }

    private static async Task RouteResolverAsync()
    {
        Console.WriteLine("  5.4 RouteTenantResolver (/api/{tenantId}/...)");
        Console.WriteLine("  ---------------------------------------------");
        var options = Options.Create(new MultiTenancyOptions());

        var withRoute = ContextWith(ctx => ctx.Request.RouteValues["tenantId"] = "contoso");
        var resolver = new RouteTenantResolver(Accessor(withRoute), options);
        Console.WriteLine($"    tenantId=contoso    → \"{await resolver.ResolveAsync()}\"");

        var noRoute = new RouteTenantResolver(Accessor(ContextWith(_ => { })), options);
        Console.WriteLine($"    No route value      → {Fmt(await noRoute.ResolveAsync())}");
        Console.WriteLine();
    }

    private static DefaultHttpContext ContextWith(Action<DefaultHttpContext> configure)
    {
        var ctx = new DefaultHttpContext();
        configure(ctx);
        return ctx;
    }

    private static IHttpContextAccessor Accessor(HttpContext ctx) => new HttpContextAccessor { HttpContext = ctx };

    private static string Fmt(string? value) => value is null ? "null" : $"\"{value}\"";
}
