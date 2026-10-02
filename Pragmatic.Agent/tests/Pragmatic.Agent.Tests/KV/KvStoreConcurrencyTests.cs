using System.Threading.Channels;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

/// <summary>
///     Tests the atomicity of the compare-and-swap write path and the eager watch cleanup —
///     the TOCTOU hardening applied to <see cref="KvStore"/> this session. The version-check and
///     dictionary write happen under one lock, so concurrent CAS writers contending on the same
///     expected version must produce exactly one winner.
/// </summary>
public class KvStoreConcurrencyTests
{
    private readonly KvStore _store = new();

    [Fact]
    public async Task Set_CAS_ConcurrentWriters_OnlyOneWins()
    {
        var (baseVersion, _) = _store.Set("k", "init");

        var winners = 0;
        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            var (_, conflict) = _store.Set("k", $"val-{i}", expectedVersion: baseVersion);
            if (!conflict)
                Interlocked.Increment(ref winners);
        }));

        await Task.WhenAll(tasks);

        // Exactly one writer observed version == baseVersion; the rest saw the incremented clock.
        winners.Should().Be(1);
    }

    [Fact]
    public void Set_CAS_NewKey_ExpectedVersionZero_Succeeds()
    {
        // An absent key has an implied version of 0, so CAS create-if-absent is expressed as
        // expectedVersion: 0.
        var (version, conflict) = _store.Set("fresh", "v", expectedVersion: 0);

        conflict.Should().BeFalse();
        version.Should().BeGreaterThan(0);
        _store.Get("fresh")!.Value.Should().Be("v");
    }

    [Fact]
    public void Set_CAS_NewKey_ExpectedVersionNonZero_Conflicts()
    {
        var (_, conflict) = _store.Set("fresh", "v", expectedVersion: 7);

        conflict.Should().BeTrue();
        _store.Get("fresh").Should().BeNull();
    }

    [Fact]
    public void Watch_AfterDispose_StopsReceivingAndCompletesChannel()
    {
        var channel = Channel.CreateUnbounded<KvChangeEvent>();
        var sub = _store.Watch("c/", channel);

        sub.Dispose();
        _store.Set("c/key", "value");

        channel.Reader.TryRead(out _).Should().BeFalse();
        channel.Reader.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Set_ConcurrentDistinctKeys_AllAppliedWithUniqueVersions()
    {
        var versions = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var (version, _) = _store.Set($"key-{i}", $"v-{i}");
            versions.Add(version);
        }));

        await Task.WhenAll(tasks);

        _store.GetAll().Should().HaveCount(100);
        // The Lamport clock is monotonic — every write got a distinct version.
        versions.Distinct().Should().HaveCount(100);
    }
}
