using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

public sealed class DatabaseConfigurationStoreTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IConfigurationStore _store;
    private readonly ServiceProvider _sp;

    public DatabaseConfigurationStoreTests()
    {
        _factory = new SqliteConnectionFactory();
        var services = new ServiceCollection();

        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.AutoCreateSchema = true;
            opts.AuditUser = "test-user";
        });

        services.AddLogging();
        _sp = services.BuildServiceProvider();
        _store = _sp.GetRequiredService<IConfigurationStore>();
    }

    [Fact]
    public async Task GetAsync_NoValue_ReturnsNull()
    {
        var result = await _store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAndGet_BaseConfig_RoundTrips()
    {
        await _store.SetAsync("App:Timeout", "30");
        var result = await _store.GetAsync("App:Timeout");
        result.Should().Be("30");
    }

    [Fact]
    public async Task SetAsync_Update_OverwritesValue()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.SetAsync("App:Timeout", "60");

        var result = await _store.GetAsync("App:Timeout");
        result.Should().Be("60");
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesValue()
    {
        await _store.SetAsync("App:Timeout", "30");
        await _store.DeleteAsync("App:Timeout");

        var result = await _store.GetAsync("App:Timeout");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        var act = () => _store.DeleteAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetSectionAsync_ReturnsMatchingKeys()
    {
        await _store.SetAsync("App:Database:Host", "localhost");
        await _store.SetAsync("App:Database:Port", "5432");
        await _store.SetAsync("App:Cache:Ttl", "300");

        var section = await _store.GetSectionAsync("App:Database:");
        section.Should().HaveCount(2);
        section["App:Database:Host"].Should().Be("localhost");
        section["App:Database:Port"].Should().Be("5432");
    }

    [Fact]
    public async Task TenantIsolation_DifferentTenantsSeeOwnValues()
    {
        await _store.SetAsync("Theme", "default");
        await _store.SetAsync("Theme", "dark", "tenant-A");
        await _store.SetAsync("Theme", "light", "tenant-B");

        var baseValue = await _store.GetAsync("Theme");
        var tenantA = await _store.GetAsync("Theme", "tenant-A");
        var tenantB = await _store.GetAsync("Theme", "tenant-B");

        baseValue.Should().Be("default");
        tenantA.Should().Be("dark");
        tenantB.Should().Be("light");
    }

    [Fact]
    public async Task TenantIsolation_DeleteOnlyAffectsTenant()
    {
        await _store.SetAsync("Key", "base-value");
        await _store.SetAsync("Key", "tenant-value", "tenant-A");

        await _store.DeleteAsync("Key", "tenant-A");

        var baseValue = await _store.GetAsync("Key");
        var tenantValue = await _store.GetAsync("Key", "tenant-A");

        baseValue.Should().Be("base-value");
        tenantValue.Should().BeNull();
    }

    [Fact]
    public async Task GetSectionAsync_WithTenant_ReturnsOnlyTenantValues()
    {
        await _store.SetAsync("App:Setting1", "base");
        await _store.SetAsync("App:Setting1", "tenant-val", "tenant-A");
        await _store.SetAsync("App:Setting2", "base-only");

        var tenantSection = await _store.GetSectionAsync("App:", "tenant-A");
        tenantSection.Should().HaveCount(1);
        tenantSection["App:Setting1"].Should().Be("tenant-val");
    }

    public void Dispose()
    {
        _sp.Dispose();
        _factory.Dispose();
    }
}
