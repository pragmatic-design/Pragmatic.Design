using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Tests.Unit;

public class InMemorySecretStoreTests
{
    private readonly InMemorySecretStore _store = new();

    [Fact]
    public async Task GetSecretAsync_NonExistent_ReturnsNull()
    {
        var result = await _store.GetSecretAsync("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecret_ThenGet_ReturnsValue()
    {
        _store.SetSecret("api-key", "sk-123456");

        var result = await _store.GetSecretAsync("api-key");
        result.Should().Be("sk-123456");
    }

    [Fact]
    public async Task SetSecret_TenantSpecific_ReturnsTenantValue()
    {
        _store.SetSecret("api-key", "base-key");
        _store.SetSecret("api-key", "tenant-key", "tenant-1");

        var baseResult = await _store.GetSecretAsync("api-key");
        var tenantResult = await _store.GetSecretAsync("api-key", "tenant-1");

        baseResult.Should().Be("base-key");
        tenantResult.Should().Be("tenant-key");
    }

    [Fact]
    public async Task GetSecretAsync_TenantWithoutOverride_FallsBackToBase()
    {
        _store.SetSecret("api-key", "base-key");

        var result = await _store.GetSecretAsync("api-key", "tenant-no-override");
        result.Should().Be("base-key");
    }

    [Fact]
    public async Task SetSecret_Overwrite_ReturnsNewValue()
    {
        _store.SetSecret("api-key", "old");
        _store.SetSecret("api-key", "new");

        var result = await _store.GetSecretAsync("api-key");
        result.Should().Be("new");
    }

    [Fact]
    public async Task IWritableSecretStore_SetThenDelete_RoundTrips()
    {
        IWritableSecretStore writable = _store;

        await writable.SetSecretAsync("api-key", "sk-write");
        (await _store.GetSecretAsync("api-key")).Should().Be("sk-write");

        await writable.DeleteSecretAsync("api-key");
        (await _store.GetSecretAsync("api-key")).Should().BeNull();
    }

    [Fact]
    public async Task IWritableSecretStore_DeleteTenantScoped_DoesNotAffectBase()
    {
        IWritableSecretStore writable = _store;
        await writable.SetSecretAsync("api-key", "base");
        await writable.SetSecretAsync("api-key", "tenant", "t1");

        await writable.DeleteSecretAsync("api-key", "t1");

        (await _store.GetSecretAsync("api-key", "t1")).Should().Be("base", "tenant delete falls back to base");
        (await _store.GetSecretAsync("api-key")).Should().Be("base");
    }
}
