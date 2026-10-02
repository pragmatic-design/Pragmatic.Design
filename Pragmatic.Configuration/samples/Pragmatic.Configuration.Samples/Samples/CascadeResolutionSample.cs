using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     <see cref="EnvironmentProfile"/> resolution chain plus <see cref="ConfigurationResolver"/>
///     cascade: a single logical key resolves through <c>tenant → environment overlay → base</c>,
///     with the most specific match winning.
/// </summary>
public static class CascadeResolutionSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Cascade Resolution — EnvironmentProfile + ConfigurationResolver");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ── EnvironmentProfile and its resolution chain ────────────────────────
        var profile = EnvironmentProfile.From("Staging", tag: "eu-west");
        Console.WriteLine($"  Profile: Name={profile.Name}, Tag={profile.Tag}");
        Console.WriteLine($"    IsStaging={profile.IsStaging}, IsProduction={profile.IsProduction}");
        Console.WriteLine($"    ResolutionChain = [{string.Join(", ", profile.ResolutionChain)}]");
        Console.WriteLine("    (overlays apply most-general → most-specific; last wins)");
        Console.WriteLine();

        // ── Seed a store with base + environment-overlay values ────────────────
        // Overlay keys are prefixed by the chain segment, e.g. "staging/api:timeout".
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("api:timeout", "30");                       // base
        await store.SetAsync("staging/api:timeout", "15");              // staging overlay
        await store.SetAsync("staging-eu-west/api:timeout", "10");      // staging + tag overlay
        await store.SetAsync("api:retries", "3");                       // base only

        // ── Resolve WITHOUT a tenant ────────────────────────────────────────────
        var resolver = new ConfigurationResolver(store, profile);

        Console.WriteLine("  Resolution (no tenant), profile = Staging/eu-west:");
        Console.WriteLine($"    api:timeout -> {await resolver.ResolveAsync("api:timeout")}  (staging-eu-west overlay wins over base 30)");
        Console.WriteLine($"    api:retries -> {await resolver.ResolveAsync("api:retries")}  (only base exists)");
        Console.WriteLine();

        // ── Resolve WITH a tenant override (highest priority) ──────────────────
        await store.SetAsync("api:timeout", "5", tenantId: "tenant-acme");

        var tenantContext = new ResolvedTenantContext("tenant-acme", "ACME Corp");
        var tenantResolver = new ConfigurationResolver(store, profile, tenantContext);

        Console.WriteLine("  Resolution for tenant 'tenant-acme':");
        Console.WriteLine($"    api:timeout -> {await tenantResolver.ResolveAsync("api:timeout")}  (tenant override wins over every overlay)");
        Console.WriteLine($"    api:retries -> {await tenantResolver.ResolveAsync("api:retries")}  (no tenant value → cascades to base)");
        Console.WriteLine();
    }

    /// <summary>Minimal resolved <see cref="ITenantContext"/> for the cascade demo.</summary>
    private sealed class ResolvedTenantContext(string tenantId, string tenantName) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantName;
        public bool IsResolved => true;
    }
}
