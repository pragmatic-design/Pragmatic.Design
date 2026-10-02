using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.MultiTenancy Samples                   ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── TenantScope ──────────────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("1. TenantScope — AsyncLocal Scoping (Background Jobs, Tests)");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

Console.WriteLine("  1.1 Begin/end scope — scoped tenant context");
Console.WriteLine("  -----------------------------------------------");

// TenantScope uses AsyncLocal — works in non-HTTP contexts
// Access resolved tenant via an instance (implements ITenantContext)
var tenantCtx = new TenantScope();

using (TenantScope.BeginScope("tenant-42", "Acme Corp"))
{
    Console.WriteLine($"    Inside scope: TenantId = \"{tenantCtx.TenantId}\"");
    Console.WriteLine($"                  TenantName = \"{tenantCtx.TenantName}\"");
    Console.WriteLine($"                  IsResolved = {tenantCtx.IsResolved}");
}

Console.WriteLine($"    Outside scope: IsResolved = {tenantCtx.IsResolved}");
Console.WriteLine();

Console.WriteLine("  1.2 Nested scopes — inner overrides outer");
Console.WriteLine("  -----------------------------------------------");

using (TenantScope.BeginScope("tenant-A", "Company A"))
{
    Console.WriteLine($"    Outer: \"{tenantCtx.TenantId}\"");

    using (TenantScope.BeginScope("tenant-B", "Company B"))
    {
        Console.WriteLine($"    Inner: \"{tenantCtx.TenantId}\" (overrides)");
    }

    Console.WriteLine($"    After inner disposed: \"{tenantCtx.TenantId}\" (restored)");
}

Console.WriteLine();

Console.WriteLine("  1.3 Async flow — scope flows across await boundaries");
Console.WriteLine("  --------------------------------------------------------");

using (TenantScope.BeginScope("async-tenant", "Async Corp"))
{
    Console.WriteLine($"    Before await: \"{tenantCtx.TenantId}\"");
    await Task.Delay(10); // Simulated async operation
    Console.WriteLine($"    After await:  \"{tenantCtx.TenantId}\" (AsyncLocal preserved!)");
}

Console.WriteLine();

// ── Resolver Strategies ──────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("2. Tenant Resolution Strategies (ASP.NET Core)");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("""
    // Header-based (default): X-Tenant-Id header
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Subdomain: acme.app.com → "acme"
    app.UseMultiTenancy(mt => mt.UseSubdomain());

    // JWT claim: "tenant_id" claim from token
    app.UseMultiTenancy(mt => mt.UseClaim());

    // Route parameter: /api/{tenantId}/orders
    app.UseMultiTenancy(mt => mt.UseRoute());

    // Composite (chain of responsibility):
    app.UseMultiTenancy(mt =>
    {
        mt.UseHeader();      // Try header first
        mt.UseClaim();       // Fallback to JWT claim
        mt.UseSubdomain();   // Last resort
    });

    // Options:
    services.Configure<MultiTenancyOptions>(o =>
    {
        o.DefaultTenantId = "default";
        o.TenantHeaderName = "X-Tenant-Id";
        o.RequireTenant = true;  // 400 if not resolved
    });
""");
Console.WriteLine();

// ── DI Usage ─────────────────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("3. DI Usage — ITenantContext Injection");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("""
    // Inject ITenantContext anywhere (scoped per request):
    public class OrderService(ITenantContext tenant)
    {
        public async Task<Order> CreateAsync(CreateOrderDto dto)
        {
            var order = new Order
            {
                TenantId = tenant.TenantId,  // Auto-set from resolved tenant
                // ...
            };
            return order;
        }
    }

    // Background job with TenantScope:
    using (TenantScope.BeginScope(tenantId, tenantName))
    {
        await ProcessTenantDataAsync();
        // ITenantContext resolves from TenantScope.Current in non-HTTP contexts
    }
""");
Console.WriteLine();

// ── Executed samples (sections 4-10) ─────────────────────────────────────────

DiWiringSample.Run();
await HttpResolversSample.RunAsync();
await MiddlewareSample.RunAsync();
await CompositeResolverSample.RunAsync();
await TenantStoreSample.RunAsync();
OptionsSample.Run();
await DbPerTenantSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
