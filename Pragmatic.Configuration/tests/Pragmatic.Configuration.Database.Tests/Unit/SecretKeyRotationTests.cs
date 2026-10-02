using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Encryption;

namespace Pragmatic.Configuration.Database.Tests.Unit;

/// <summary>
///     End-to-end key rotation over a shared in-memory SQLite database: secrets written under an old key are
///     re-encrypted under a new key by <see cref="ISecretKeyRotationService" />, after which a store holding
///     only the new key can read them.
/// </summary>
public sealed class SecretKeyRotationTests : IDisposable
{
    private static readonly string OldKey = Convert.ToBase64String(Filled(0xA1));
    private static readonly string NewKey = Convert.ToBase64String(Filled(0xB2));

    private readonly SqliteConnectionFactory _factory = new();
    private readonly List<ServiceProvider> _providers = [];

    private static byte[] Filled(byte b)
    {
        var key = new byte[32];
        Array.Fill(key, b);
        return key;
    }

    private ServiceProvider Build(string encryptionKey, params string[] previousKeys)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.AutoCreateSchema = true;
            opts.EncryptionKey = encryptionKey;
            foreach (var k in previousKeys)
                opts.PreviousEncryptionKeys.Add(k);
        });
        services.AddDatabaseSecretStore();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        _providers.Add(sp);
        return sp;
    }

    [Fact]
    public async Task ReEncryptAll_MovesSecretsToNewKey_ReadableByNewKeyOnly()
    {
        // 1. Write under the old key (the DB store implements both read and write contracts).
        var oldStore = (IWritableSecretStore)Build(OldKey).GetRequiredService<ISecretStore>();
        await oldStore.SetSecretAsync("db-password", "s3cret");
        await oldStore.SetSecretAsync("api-key", "tok-42", "tenant-A");

        // 2. A store with ONLY the new key cannot read old-key values (fails closed → null).
        var newOnly = Build(NewKey).GetRequiredService<ISecretStore>();
        (await newOnly.GetSecretAsync("db-password")).Should().BeNull("value is still under the old key");

        // 3. Rotate: new key current, old key kept as previous → re-encrypt pass.
        var rotated = Build(NewKey, OldKey);
        var report = await rotated.GetRequiredService<ISecretKeyRotationService>().ReEncryptAllAsync();

        report.Total.Should().Be(2);
        report.ReEncrypted.Should().Be(2);
        report.Failed.Should().Be(0);

        // 4. Now the new-key-only store reads everything — proving values were rewritten under the new key.
        (await newOnly.GetSecretAsync("db-password")).Should().Be("s3cret");
        (await newOnly.GetSecretAsync("api-key", "tenant-A")).Should().Be("tok-42");
    }

    [Fact]
    public async Task ReEncryptAll_NoSecrets_ReportsZero()
    {
        var report = await Build(NewKey).GetRequiredService<ISecretKeyRotationService>().ReEncryptAllAsync();

        report.Should().Be(new SecretRotationReport(0, 0, 0));
    }

    public void Dispose()
    {
        foreach (var sp in _providers)
            sp.Dispose();
        _factory.Dispose();
    }
}
