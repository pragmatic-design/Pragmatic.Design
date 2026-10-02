using Pragmatic.Configuration.Providers;

namespace Showcase.Host.Configuration;

/// <summary>
/// Seeds demo configuration values and secrets on startup.
/// In production, these would come from a database, Azure App Configuration, or Key Vault.
/// Demonstrates: IConfigurationStore with tenant-specific overrides + ISecretStore for sensitive data.
/// </summary>
public class ShowcaseConfigurationSeeder(
    IConfigurationStore configStore,
    ISecretStore secretStore,
    ILogger<ShowcaseConfigurationSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // --- Configuration values ---

        // Base cancellation window (applies to all tenants by default)
        await configStore.SetAsync("Booking:CancellationWindowHours", "24", ct: ct).ConfigureAwait(false);

        // Tenant-specific override: premium tenants get extended cancellation
        await configStore.SetAsync("Booking:CancellationWindowHours", "72", "premium-hotel", ct).ConfigureAwait(false);
        await configStore.SetAsync("Booking:CancellationWindowHours", "48", "grand-resort", ct).ConfigureAwait(false);

        // Base max occupancy multiplier
        await configStore.SetAsync("Booking:MaxOccupancyMultiplier", "1.0", ct: ct).ConfigureAwait(false);

        // --- Secrets ---

        // Seed payment provider API keys into the secret store
        // In production, these would be in Azure Key Vault, HashiCorp Vault, or encrypted DB
        if (secretStore is InMemorySecretStore memorySecrets)
        {
            memorySecrets.SetSecret("Payment:StripeApiKey", "sk_test_showcase_stripe_key_2026");
            memorySecrets.SetSecret("Payment:StripeWebhookSecret", "whsec_showcase_webhook_secret");
        }

        logger.LogInformation("Seeded demo configuration values and secrets");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
