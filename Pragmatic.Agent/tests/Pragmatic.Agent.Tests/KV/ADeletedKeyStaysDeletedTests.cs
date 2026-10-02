using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

/// <summary>
///     A delete leaves a tombstone: a gossiped write older than the delete, arriving late from
///     an Agent that missed the delete, does not bring the key back.
/// </summary>
public sealed class ADeletedKeyStaysDeletedTests
{
    [Fact]
    public void AnOlderWriteArrivingAfterTheDelete_IsRefused()
    {
        var store = new KvStore();
        var (written, _) = store.Set("gateway/instances/warehouse/b", "http://b");
        store.Delete("gateway/instances/warehouse/b");

        var applied = store.SetIfNewer("gateway/instances/warehouse/b", "http://b", written, DateTimeOffset.UtcNow);

        applied.Should().BeFalse();
        store.Get("gateway/instances/warehouse/b").Should().BeNull("the delete is newer than the write that came back");
    }

    [Fact]
    public void AGossipedDeleteForAKeyNeverHeld_StillRefusesTheOlderWrite()
    {
        var store = new KvStore();

        store.DeleteIfNewer("gateway/instances/warehouse/b", version: 20);
        store.SetIfNewer("gateway/instances/warehouse/b", "http://b", version: 10, DateTimeOffset.UtcNow);

        store.Get("gateway/instances/warehouse/b").Should().BeNull("the write arrived after its delete");
    }

    /// <summary>The control: a write newer than the delete is a new value, and stands.</summary>
    [Fact]
    public void ANewerWriteAfterTheDelete_IsAccepted()
    {
        var store = new KvStore();
        store.Set("gateway/instances/warehouse/b", "http://b");
        store.Delete("gateway/instances/warehouse/b");
        var tombstone = store.GetTombstones().Single(t => t.Key == "gateway/instances/warehouse/b");

        store.SetIfNewer("gateway/instances/warehouse/b", "http://b2", tombstone.Version + 1, DateTimeOffset.UtcNow);

        store.Get("gateway/instances/warehouse/b")!.Value.Should().Be("http://b2");
        store.GetTombstones().Should().BeEmpty("the key lives again");
    }

    /// <summary>The control: a local write after a delete always stands — it takes a fresh version.</summary>
    [Fact]
    public void ALocalWriteAfterTheDelete_Stands()
    {
        var store = new KvStore();
        store.Set("config/limit", "10");
        store.Delete("config/limit");

        store.Set("config/limit", "20");

        store.Get("config/limit")!.Value.Should().Be("20");
    }

    [Fact]
    public void ATombstoneOlderThanItsRetention_IsCollected()
    {
        var store = new KvStore();
        store.Set("config/limit", "10");
        store.Delete("config/limit");

        store.CollectTombstones(olderThan: DateTimeOffset.UtcNow.AddSeconds(1));

        store.GetTombstones().Should().BeEmpty();
    }

    /// <summary>The control: a tombstone within its retention is kept.</summary>
    [Fact]
    public void ATombstoneWithinItsRetention_IsKept()
    {
        var store = new KvStore();
        store.Set("config/limit", "10");
        store.Delete("config/limit");

        store.CollectTombstones(olderThan: DateTimeOffset.UtcNow.AddMinutes(-10));

        store.GetTombstones().Select(t => t.Key).Should().BeEquivalentTo("config/limit");
    }
}
