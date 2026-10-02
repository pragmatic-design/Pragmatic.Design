using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Azure;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Azure backends: Azure App Configuration as the <see cref="IConfigurationStore"/> and
///     Azure Key Vault as the <see cref="ISecretStore"/>, wired via
///     <see cref="AzureConfigurationExtensions"/>.
/// </summary>
/// <remarks>
///     This sample shows the registration and usage shape only — it does NOT connect to Azure.
///     The actual <c>GetAsync</c> / <c>GetSecretAsync</c> calls require a live App Configuration
///     resource and Key Vault, plus an authenticated <c>DefaultAzureCredential</c> (managed identity,
///     Azure CLI login, or environment credentials). Set real endpoints/URIs and uncomment the calls
///     at the bottom to run against your own resources. The credential-based path
///     (<see cref="AzureConfigurationOptions.AppConfigurationEndpoint"/> +
///     <see cref="AzureConfigurationOptions.KeyVaultUri"/>) is preferred over connection strings,
///     which embed a key in plaintext.
/// </remarks>
public static class AzureSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Azure Backends — App Configuration + Key Vault (setup only)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();

        // ── App Configuration + Key Vault via managed identity (recommended) ───
        services.AddAzureConfiguration(options =>
        {
            // Endpoint + DefaultAzureCredential — no secrets in config.
            options.AppConfigurationEndpoint = "https://my-appconfig.azconfig.io";
            options.KeyVaultUri = "https://my-vault.vault.azure.net";

            options.KeyPrefix = "Pragmatic";          // namespacing for all keys
            options.SentinelKey = "Pragmatic:Sentinel"; // bump to trigger a refresh
            options.CacheExpiration = TimeSpan.FromSeconds(30);
            options.SecretCacheExpiration = TimeSpan.FromMinutes(5);
            options.NegativeCacheExpiration = TimeSpan.FromSeconds(30); // cache 404s briefly
        });

        Console.WriteLine("  Registered AddAzureConfiguration():");
        Console.WriteLine("    IConfigurationStore -> AzureAppConfigurationStore (sentinel-based change polling)");
        Console.WriteLine("    ISecretStore        -> AzureKeyVaultSecretStore   (DefaultAzureCredential)");
        Console.WriteLine();

        // Key-Vault-only registration is also available:
        //   services.AddAzureKeyVaultSecretStore(o => o.KeyVaultUri = "https://my-vault.vault.azure.net");

        // The container builds without contacting Azure; clients connect lazily on first use.
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IConfigurationStore>();
        var secrets = sp.GetRequiredService<ISecretStore>();
        Console.WriteLine($"    Resolved store type:  {store.GetType().Name}");
        Console.WriteLine($"    Resolved secret type: {secrets.GetType().Name}");
        Console.WriteLine();

        Console.WriteLine("  To run against real resources (requires Azure auth), use:");
        Console.WriteLine("    var v = await store.GetAsync(\"Smtp:Host\");");
        Console.WriteLine("    var p = await secrets.GetSecretAsync(\"Db:Password\");");
        Console.WriteLine();

        // Live calls (left commented — need a real App Config resource + Key Vault + credentials):
        //   var value  = await store.GetAsync("Smtp:Host");
        //   var secret = await secrets.GetSecretAsync("Db:Password");
    }
}
