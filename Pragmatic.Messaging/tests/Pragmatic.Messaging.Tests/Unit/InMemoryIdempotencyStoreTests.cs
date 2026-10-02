using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

public class InMemoryIdempotencyStoreTests
{
    [Fact]
    public async Task TryMarkAsProcessed_FirstTime_ShouldReturnTrue()
    {
        var store = new InMemoryIdempotencyStore();
        var result = await store.TryMarkAsProcessedAsync("msg-1");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryMarkAsProcessed_SecondTime_ShouldReturnFalse()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryMarkAsProcessedAsync("msg-1");
        var result = await store.TryMarkAsProcessedAsync("msg-1");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryMarkAsProcessed_DifferentIds_ShouldBothReturnTrue()
    {
        var store = new InMemoryIdempotencyStore();
        var r1 = await store.TryMarkAsProcessedAsync("msg-1");
        var r2 = await store.TryMarkAsProcessedAsync("msg-2");
        r1.Should().BeTrue();
        r2.Should().BeTrue();
        store.Count.Should().Be(2);
    }

    [Fact]
    public async Task Remove_ReleasesClaim_AllowingReClaim()
    {
        // C3: when delivery fails after claiming, RemoveAsync releases the claim so a retry/redelivery
        // can re-attempt instead of the message being treated as an already-processed duplicate.
        var store = new InMemoryIdempotencyStore();

        (await store.TryMarkAsProcessedAsync("msg-1")).Should().BeTrue();
        (await store.TryMarkAsProcessedAsync("msg-1")).Should().BeFalse("already claimed");

        await store.RemoveAsync("msg-1");

        (await store.TryMarkAsProcessedAsync("msg-1")).Should().BeTrue("the claim was released");
    }

    [Fact]
    public async Task Remove_UnknownId_IsNoOp()
    {
        var store = new InMemoryIdempotencyStore();
        await store.RemoveAsync("never-seen"); // must not throw
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeOlderThan_ShouldRemoveExpiredEntries()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryMarkAsProcessedAsync("old-msg");

        // Purge with zero age removes everything
        await store.PurgeOlderThanAsync(TimeSpan.Zero);

        // The entry should be purged, so re-adding should succeed
        var result = await store.TryMarkAsProcessedAsync("old-msg");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Count_ShouldReflectStoredEntries()
    {
        var store = new InMemoryIdempotencyStore();
        store.Count.Should().Be(0);

        await store.TryMarkAsProcessedAsync("msg-1");
        await store.TryMarkAsProcessedAsync("msg-2");
        store.Count.Should().Be(2);

        // Duplicate doesn't increase count
        await store.TryMarkAsProcessedAsync("msg-1");
        store.Count.Should().Be(2);
    }
}
