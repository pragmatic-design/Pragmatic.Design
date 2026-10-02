using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Real <see cref="IConfigurationStore"/> operations against the default
///     <see cref="InMemoryConfigurationStore"/>: get/set/delete, section reads, and
///     multi-tenant overrides where a tenant-scoped value shadows the global default.
/// </summary>
public static class InMemoryStoreSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. IConfigurationStore — Real Get/Set/Delete + Multi-Tenant");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        IConfigurationStore store = new InMemoryConfigurationStore();

        // ── Base values ───────────────────────────────────────────────────────
        await store.SetAsync("smtp:host", "mail.example.com");
        await store.SetAsync("smtp:port", "587");

        Console.WriteLine("  Base configuration:");
        Console.WriteLine($"    smtp:host = {await store.GetAsync("smtp:host")}");
        Console.WriteLine($"    smtp:port = {await store.GetAsync("smtp:port")}");
        Console.WriteLine();

        // ── Section read (prefix scan) ──────────────────────────────────────────
        var section = await store.GetSectionAsync("smtp:");
        Console.WriteLine($"  Section 'smtp:' contains {section.Count} keys:");
        foreach (var (key, value) in section.OrderBy(kv => kv.Key))
            Console.WriteLine($"    {key} = {value}");
        Console.WriteLine();

        // ── Multi-tenant override ───────────────────────────────────────────────
        // tenant1 gets its own SMTP host; unset keys fall back to the base value.
        await store.SetAsync("smtp:host", "tenant1.smtp.com", tenantId: "tenant1");

        Console.WriteLine("  Multi-tenant resolution:");
        Console.WriteLine($"    smtp:host (global)  = {await store.GetAsync("smtp:host")}");
        Console.WriteLine($"    smtp:host (tenant1) = {await store.GetAsync("smtp:host", "tenant1")}");
        Console.WriteLine($"    smtp:port (tenant1) = {await store.GetAsync("smtp:port", "tenant1")}  (falls back to base)");
        Console.WriteLine();

        // ── Delete ──────────────────────────────────────────────────────────────
        await store.DeleteAsync("smtp:port");
        Console.WriteLine($"  After delete: smtp:port = {await store.GetAsync("smtp:port") ?? "(null)"}");
        Console.WriteLine();
    }
}
