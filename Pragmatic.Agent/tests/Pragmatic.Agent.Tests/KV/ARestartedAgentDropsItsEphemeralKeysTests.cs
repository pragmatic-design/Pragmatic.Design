using Pragmatic.Agent.KV;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

/// <summary>
///     An Agent that restarts does not bring back the ephemeral keys it persisted: no client of
///     the previous process is connected to the new one.
/// </summary>
public sealed class ARestartedAgentDropsItsEphemeralKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pragmatic-prag630-{Guid.NewGuid():N}");

    [Fact]
    public void AReloadedStore_HoldsThePlainKeys_AndNoneOfTheEphemeralOnes()
    {
        var path = Path.Combine(_dir, "kv.json");
        var before = new KvStore();
        before.Set("gateway/instances/warehouse/i1", "http://a", owner: "agent-a");
        before.Set("gateway/instances/warehouse/i2", "http://b", owner: "agent-b");
        before.Set("config/warehouse/limit", "10");
        new KvFilePersistence(before, path).FlushNow();

        var after = new KvStore();
        new KvFilePersistence(after, path).Load();

        after.Get("gateway/instances/warehouse/i1").Should().BeNull("its client belonged to the previous process");
        after.Get("gateway/instances/warehouse/i2").Should().BeNull(
            "one another Agent owns comes back from it by anti-entropy, if that Agent is still alive");
        after.Get("config/warehouse/limit")!.Value.Should().Be("10", "the control: a plain key survives the restart");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
