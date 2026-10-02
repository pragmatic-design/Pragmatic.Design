using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Database secret store with AES-256-GCM encryption at rest via
///     <see cref="DatabaseConfigurationExtensions.AddDatabaseSecretStore"/>. The encryption key is
///     resolved through the registered <c>IEncryptionKeyProvider</c> and validated to be exactly
///     32 bytes (256-bit) when the <c>ISecretEncryptor</c> is constructed.
/// </summary>
/// <remarks>
///     Secret <i>writes</i> are performed by the host/admin pipeline through the internal database
///     secret store; the public <see cref="ISecretStore"/> surface is read-only by design (secrets are
///     provisioned out-of-band). This sample therefore demonstrates the runnable public behavior:
///     key generation, the 32-byte validation guard firing for an invalid key, successful store
///     construction with a valid key, and a read of an absent secret.
/// </remarks>
public static class EncryptionSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Secrets at Rest — AES-256-GCM database secret store");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ── Generate a valid 256-bit (32-byte) key, base64-encoded ─────────────
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);
        Console.WriteLine($"  Generated 32-byte AES-256 key (base64): {validKey[..12]}… ({keyBytes.Length} bytes)");
        Console.WriteLine();

        // ── A wrong-length key is rejected when the encryptor is built ─────────
        Console.WriteLine("  Validation guard — a 16-byte key is rejected:");
        try
        {
            using var bad = BuildSecretStoreProvider(Convert.ToBase64String(new byte[16]));
            // The encryptor is built lazily on first resolve of ISecretStore.
            _ = bad.GetRequiredService<ISecretStore>();
            Console.WriteLine("    (unexpected: no exception thrown)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Rejected: {ex.GetType().Name} — {Trim(ex.Message)}");
        }
        Console.WriteLine();

        // ── A valid key builds the encrypted secret store successfully ─────────
        using var factory = new SqliteConnectionFactory();
        using var sp = BuildSecretStoreProvider(validKey, factory);
        var secrets = sp.GetRequiredService<ISecretStore>();
        Console.WriteLine("  Secret store constructed with a valid key (AES-256-GCM, format: [nonce][tag][ciphertext]).");

        // Reads are public; an unprovisioned secret returns null.
        var missing = await secrets.GetSecretAsync("Db:Password");
        Console.WriteLine($"    GetSecretAsync(\"Db:Password\") = {missing ?? "(null — not provisioned)"}");
        Console.WriteLine("    Writes are host/admin-driven; values are encrypted before they touch the table.");
        Console.WriteLine();
    }

    private static ServiceProvider BuildSecretStoreProvider(string base64Key, SqliteConnectionFactory? factory = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory>(factory ?? new SqliteConnectionFactory());
        services.AddDatabaseConfigurationStore(options =>
        {
            options.Provider = DatabaseProvider.Sqlite;
            options.EncryptionKey = base64Key;
        });
        services.AddDatabaseSecretStore();
        return services.BuildServiceProvider();
    }

    private static string Trim(string message)
        => message.Length <= 80 ? message : message[..80] + "…";
}
