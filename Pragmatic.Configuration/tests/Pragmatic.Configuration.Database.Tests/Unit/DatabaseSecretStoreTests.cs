using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

public sealed class DatabaseSecretStoreTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ISecretStore _store;
    private readonly ServiceProvider _sp;

    // 32-byte key base64-encoded for AES-256
    private const string TestEncryptionKey = "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=";

    public DatabaseSecretStoreTests()
    {
        _factory = new SqliteConnectionFactory();
        var services = new ServiceCollection();

        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.AutoCreateSchema = true;
            opts.EncryptionKey = TestEncryptionKey;
        });
        services.AddDatabaseSecretStore();
        services.AddLogging();

        _sp = services.BuildServiceProvider();
        _store = _sp.GetRequiredService<ISecretStore>();
    }

    [Fact]
    public async Task GetSecretAsync_NoSecret_ReturnsNull()
    {
        var result = await _store.GetSecretAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_WithTenant_NoSecret_ReturnsNull()
    {
        var result = await _store.GetSecretAsync("nonexistent", "tenant-A");
        result.Should().BeNull();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _factory.Dispose();
    }
}
