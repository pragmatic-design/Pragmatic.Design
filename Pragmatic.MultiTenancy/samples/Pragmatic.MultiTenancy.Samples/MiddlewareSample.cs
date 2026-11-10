using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Drives <see cref="TenantResolutionMiddleware"/> directly with a <see cref="DefaultHttpContext"/>
///     and a stub resolver, demonstrating the resolved path, the unresolved pass-through, and the
///     <see cref="MultiTenancyOptions.RequireTenant"/> 400 short-circuit — all without a web host.
/// </summary>
public static class MiddlewareSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. TenantResolutionMiddleware — pipeline behavior (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 6.1 Tenant resolved → context populated, request continues.
        Console.WriteLine("  6.1 Tenant resolved → MutableTenantContext populated, next() invoked");
        Console.WriteLine("  -------------------------------------------------------------------");
        await InvokeAsync(
            resolved: "tenant-acme",
            requireTenant: false);

        // 6.2 No tenant, RequireTenant=false → request flows through.
        Console.WriteLine("  6.2 No tenant, RequireTenant=false → request continues downstream");
        Console.WriteLine("  -----------------------------------------------------------------");
        await InvokeAsync(
            resolved: null,
            requireTenant: false);

        // 6.3 No tenant, RequireTenant=true → 400 with a TENANT_REQUIRED ProblemDetails, pipeline short-circuited.
        // (An unauthenticated caller on a route that requires authorization is let through instead, and answered 401.)
        Console.WriteLine("  6.3 No tenant, RequireTenant=true → HTTP 400 (TENANT_REQUIRED), next() NOT invoked");
        Console.WriteLine("  ----------------------------------------------------------------");
        await InvokeAsync(
            resolved: null,
            requireTenant: true);
    }

    private static async Task InvokeAsync(string? resolved, bool requireTenant)
    {
        // A request always has its services: the 400 is a ProblemDetails, written through them.
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Request.Path = "/api/orders";

        var nextInvoked = false;
        RequestDelegate next = _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        };

        var options = Options.Create(new MultiTenancyOptions { RequireTenant = requireTenant });
        var middleware = new TenantResolutionMiddleware(next, NullLogger<TenantResolutionMiddleware>.Instance, options);

        var tenantContext = new MutableTenantContext();
        await middleware.InvokeAsync(context, new StubResolver(resolved), tenantContext);

        Console.WriteLine($"    Resolver returned    : {(resolved is null ? "null" : $"\"{resolved}\"")}");
        Console.WriteLine($"    Context.TenantId     : {(tenantContext.TenantId is null ? "null" : $"\"{tenantContext.TenantId}\"")}");
        Console.WriteLine($"    Context.IsResolved   : {tenantContext.IsResolved}");
        Console.WriteLine($"    next() invoked       : {nextInvoked}");
        Console.WriteLine($"    Response StatusCode  : {context.Response.StatusCode}");
        Console.WriteLine();
    }

    /// <summary>Resolver that returns a fixed value (or null) — stands in for a real HTTP resolver.</summary>
    private sealed class StubResolver(string? tenantId) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(tenantId);
    }
}
