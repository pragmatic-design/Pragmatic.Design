using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.InMemory;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Exercises the thread-safety contract of <see cref="InMemoryDiscoveryBackend"/>,
/// which is backed by a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public class InMemoryDiscoveryBackendConcurrencyTests
{
    private static HostTopologyInfo Topology(string hostName)
        => new() { HostName = hostName };

    [Fact]
    public async Task StoreAsync_ConcurrentDistinctHosts_StoresAllEntries()
    {
        var backend = new InMemoryDiscoveryBackend();
        const int count = 200;

        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(() => backend.StoreAsync(Topology($"Host{i}"))));

        await Task.WhenAll(tasks);

        var all = await backend.GetAllAsync();
        all.Should().HaveCount(count);
    }

    [Fact]
    public async Task StoreAsync_ConcurrentSameHost_LeavesExactlyOneEntry()
    {
        var backend = new InMemoryDiscoveryBackend();

        var tasks = Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => backend.StoreAsync(Topology("SharedHost"))));

        await Task.WhenAll(tasks);

        var all = await backend.GetAllAsync();
        all.Should().ContainSingle();
        all[0].HostName.Should().Be("SharedHost");
    }

    [Fact]
    public async Task GetAllAsync_WhileStoring_DoesNotThrow()
    {
        var backend = new InMemoryDiscoveryBackend();

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
                await backend.StoreAsync(Topology($"Host{i}")).ConfigureAwait(false);
        });

        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
                _ = await backend.GetAllAsync().ConfigureAwait(false);
        });

        var act = async () => await Task.WhenAll(writer, reader);

        await act.Should().NotThrowAsync();
    }
}
