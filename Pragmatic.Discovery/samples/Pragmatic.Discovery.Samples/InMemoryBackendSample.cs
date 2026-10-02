// Pragmatic.Discovery Samples - InMemoryDiscoveryBackend direct usage.

using Pragmatic.Discovery.InMemory;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates the <see cref="InMemoryDiscoveryBackend"/> directly (no DI),
/// including its upsert semantics and the host-name guard.
/// </summary>
internal static class InMemoryBackendSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("InMemoryDiscoveryBackend — Direct Usage");

        var backend = new InMemoryDiscoveryBackend();

        // Store two distinct hosts.
        await backend.StoreAsync(SampleData.MainHost());
        await backend.StoreAsync(SampleData.BillingHost());
        SampleConsole.Step("Stored 'Showcase.Host' and 'Showcase.Billing.Host'.");

        var all = await backend.GetAllAsync();
        SampleConsole.Info($"GetAllAsync → {all.Count} host(s): {string.Join(", ", all.Select(h => h.HostName))}");

        // Lookup by host name.
        var billing = await backend.GetByHostNameAsync("Showcase.Billing.Host");
        SampleConsole.Info($"GetByHostNameAsync('Showcase.Billing.Host') → {billing?.Modules.Count ?? 0} module(s)");

        // Upsert: re-storing the same host name replaces the entry (never throws).
        await backend.StoreAsync(SampleData.Monolith()); // same HostName "Showcase.Host"
        var afterUpsert = await backend.GetByHostNameAsync("Showcase.Host");
        SampleConsole.Step("Re-stored 'Showcase.Host' with the monolith topology (upsert).");
        SampleConsole.Info($"'Showcase.Host' now has {afterUpsert?.Modules.Count ?? 0} module(s) (was 2).");

        var stillTwo = await backend.GetAllAsync();
        SampleConsole.Info($"Host count is still {stillTwo.Count} — upsert replaced, did not duplicate.");

        // Guard: empty host name is rejected.
        try
        {
            await backend.StoreAsync(SampleData.MainHost() with { HostName = "  " });
        }
        catch (ArgumentException ex)
        {
            SampleConsole.Step($"Empty HostName rejected: {ex.Message.Split('(')[0].Trim()}");
        }

        SampleConsole.Blank();
    }
}
