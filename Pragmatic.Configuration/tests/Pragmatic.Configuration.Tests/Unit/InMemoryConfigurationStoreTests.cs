using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Tests.Unit;

public class InMemoryConfigurationStoreTests
{
    private readonly InMemoryConfigurationStore _store = new();

    [Fact]
    public async Task GetAsync_NonExistentKey_ReturnsNull()
    {
        var result = await _store.GetAsync("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGet_ReturnsValue()
    {
        await _store.SetAsync("key1", "value1");

        var result = await _store.GetAsync("key1");
        result.Should().Be("value1");
    }

    [Fact]
    public async Task SetAsync_Overwrite_ReturnsNewValue()
    {
        await _store.SetAsync("key1", "old");
        await _store.SetAsync("key1", "new");

        var result = await _store.GetAsync("key1");
        result.Should().Be("new");
    }

    [Fact]
    public async Task DeleteAsync_RemovesValue()
    {
        await _store.SetAsync("key1", "value1");
        await _store.DeleteAsync("key1");

        var result = await _store.GetAsync("key1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        var act = () => _store.DeleteAsync("missing");
        await act.Should().NotThrowAsync();
    }

    // Tenant isolation

    [Fact]
    public async Task GetAsync_WithTenant_ReturnsTenantValue()
    {
        await _store.SetAsync("key1", "base-value");
        await _store.SetAsync("key1", "tenant-value", "tenant-1");

        var baseResult = await _store.GetAsync("key1");
        var tenantResult = await _store.GetAsync("key1", "tenant-1");

        baseResult.Should().Be("base-value");
        tenantResult.Should().Be("tenant-value");
    }

    [Fact]
    public async Task GetAsync_WithTenant_NoOverride_ReturnsNull()
    {
        await _store.SetAsync("key1", "base-value");

        // A tenant-scoped read returns only the tenant override, or null. Base fallback is the
        // resolver's cascade responsibility — folding it in here would hide the environment overlay.
        var result = await _store.GetAsync("key1", "tenant-no-override");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_Tenant_OnlyRemovesTenantValue()
    {
        await _store.SetAsync("key1", "base-value");
        await _store.SetAsync("key1", "tenant-value", "tenant-1");

        await _store.DeleteAsync("key1", "tenant-1");

        var baseResult = await _store.GetAsync("key1");
        var tenantResult = await _store.GetAsync("key1", "tenant-1");

        baseResult.Should().Be("base-value");
        tenantResult.Should().BeNull(); // tenant override removed; store does not fall back to base
    }

    // Section queries

    [Fact]
    public async Task GetSectionAsync_ReturnsMatchingKeys()
    {
        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("Booking:HotelName", "Test Hotel");
        await _store.SetAsync("Payment:ApiKey", "secret");

        var section = await _store.GetSectionAsync("Booking:");

        section.Should().HaveCount(2);
        section.Should().ContainKey("Booking:MaxGuests").WhoseValue.Should().Be("100");
        section.Should().ContainKey("Booking:HotelName").WhoseValue.Should().Be("Test Hotel");
    }

    [Fact]
    public async Task GetSectionAsync_WithTenant_ReturnsTenantSection()
    {
        await _store.SetAsync("Booking:MaxGuests", "100", "tenant-1");

        var section = await _store.GetSectionAsync("Booking:", "tenant-1");

        section.Should().HaveCount(1);
        section.Should().ContainKey("Booking:MaxGuests").WhoseValue.Should().Be("100");
    }

    [Fact]
    public async Task GetSectionAsync_EmptyPrefix_ReturnsEmpty()
    {
        var section = await _store.GetSectionAsync("NonExistent:");
        section.Should().BeEmpty();
    }

    // Watch

    [Fact]
    public async Task WatchAsync_EmitsChanges()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var changes = _store.WatchAsync("Booking:*", cts.Token).GetAsyncEnumerator(cts.Token);

        // The store registers the watcher synchronously on the first MoveNextAsync, so every write after
        // this line is seen. Waiting a fixed time instead is a race the writes can lose.
        var first = changes.MoveNextAsync();

        await _store.SetAsync("Booking:MaxGuests", "100");
        await _store.SetAsync("Payment:Key", "ignored"); // different prefix
        await _store.SetAsync("Booking:HotelName", "Test");

        (await first).Should().BeTrue();
        changes.Current.Key.Should().Be("Booking:MaxGuests");
        (await changes.MoveNextAsync()).Should().BeTrue();
        changes.Current.Key.Should().Be("Booking:HotelName");
    }
}
