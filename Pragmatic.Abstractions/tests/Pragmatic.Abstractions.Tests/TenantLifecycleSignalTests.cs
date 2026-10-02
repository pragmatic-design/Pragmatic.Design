using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The tenant lifecycle tells somebody.
/// </summary>
/// <remarks>
///     <para>
///         <c>CreateAsync</c>, <c>UpdateAsync</c>, <c>DeactivateAsync</c> and <c>DeleteAsync</c> on
///         <see cref="ITenantStore" /> tell nobody by themselves, so without the signal anything derived
///         from the set of tenants and held in memory goes stale in silence. The <c>[Lookup]</c> preload
///         holds such a thing.
///     </para>
///     <para>
///         The signal is raised by a decorator rather than by each store, because "each store raises
///         it" would be three hand-written copies and a fourth in whichever store an application writes.
///     </para>
///     <para>
///         The notification is <b>awaited</b>, unlike the maintenance observer it is modelled on: a
///         caller that creates a tenant and then serves a request for it must not race the thing that
///         derives from the tenant set. Observers are isolated from each other and from the store — one
///         that throws is logged and ignored, because a refresh that fails must not report a write that
///         already happened as a write that did not.
///     </para>
/// </remarks>
public sealed class TenantLifecycleSignalTests
{
    /// <summary>Creating a tenant tells the observers, with the tenant that was created.</summary>
    [Fact]
    public async Task CreatingATenant_TellsTheObservers()
    {
        var observer = new RecordingObserver();
        var store = new ObservedTenantStore(new StubTenantStore(), [observer]);

        await store.CreateAsync(Tenant("tenant-a"));

        observer.Events.Should().BeEquivalentTo(["created:tenant-a"]);
    }

    /// <summary>Updating, deactivating and deleting do too, each under its own name.</summary>
    /// <remarks>
    ///     Asked as one case because the claim is the set: a consumer that reacts to a tenant appearing
    ///     has to hear the ways one goes away, and hearing them all as "something changed" would make
    ///     every reaction guess which.
    /// </remarks>
    [Fact]
    public async Task TheOtherThreeTransitions_EachArriveUnderTheirOwnName()
    {
        var observer = new RecordingObserver();
        var store = new ObservedTenantStore(new StubTenantStore(), [observer]);

        await store.UpdateAsync(Tenant("tenant-a"));
        await store.DeactivateAsync("tenant-b");
        await store.DeleteAsync("tenant-c");

        observer.Events.Should().BeEquivalentTo(
            ["updated:tenant-a", "deactivated:tenant-b", "deleted:tenant-c"]);
    }

    /// <summary>The control: reading the store tells nobody.</summary>
    /// <remarks>
    ///     Without it, "the lifecycle emits a signal" is satisfied by a decorator that fires on every
    ///     call — which would have every consumer refresh on every read of the tenant list, and the
    ///     preload reads it at startup.
    /// </remarks>
    [Fact]
    public async Task ReadingTheStore_TellsNobody()
    {
        var observer = new RecordingObserver();
        var store = new ObservedTenantStore(new StubTenantStore(), [observer]);

        await store.GetByIdAsync("tenant-a");
        await store.GetAllAsync();
        await store.GetActiveAsync();

        observer.Events.Should().BeEmpty();
    }

    /// <summary>The second control: a write that changed nothing tells nobody either.</summary>
    /// <remarks>
    ///     <c>UpdateAsync</c>, <c>DeactivateAsync</c> and <c>DeleteAsync</c> return false when there was
    ///     no such tenant. Announcing a transition that did not happen would have a consumer drop what
    ///     it derived for a tenant that is still active.
    /// </remarks>
    [Fact]
    public async Task AWriteThatChangedNothing_TellsNobody()
    {
        var observer = new RecordingObserver();
        var store = new ObservedTenantStore(new StubTenantStore { WritesSucceed = false }, [observer]);

        await store.UpdateAsync(Tenant("tenant-a"));
        await store.DeactivateAsync("tenant-a");
        await store.DeleteAsync("tenant-a");

        observer.Events.Should().BeEmpty();
    }

    /// <summary>An observer that throws is ignored, and the others still hear it.</summary>
    [Fact]
    public async Task AnObserverThatThrows_NeitherFailsTheWriteNorSilencesTheRest()
    {
        var observer = new RecordingObserver();
        var store = new ObservedTenantStore(new StubTenantStore(), [new ThrowingObserver(), observer]);

        var created = await store.CreateAsync(Tenant("tenant-a"));

        created.TenantId.Should().Be("tenant-a");
        observer.Events.Should().BeEquivalentTo(["created:tenant-a"]);
    }

    /// <summary>The reads still answer from the store underneath.</summary>
    /// <remarks>
    ///     A decorator is only correct if it is transparent; this is the case that would catch one that
    ///     forwards the writes and answers the reads itself.
    /// </remarks>
    [Fact]
    public async Task TheDecorator_ForwardsEveryReadToTheStoreUnderneath()
    {
        var inner = new StubTenantStore();
        await inner.CreateAsync(Tenant("tenant-a"));
        var store = new ObservedTenantStore(inner, []);

        (await store.GetByIdAsync("tenant-a"))!.TenantId.Should().Be("tenant-a");
        (await store.GetAllAsync()).Should().HaveCount(1);
        (await store.GetActiveAsync()).Should().HaveCount(1);
    }

    private static TenantInfo Tenant(string id) => new()
    {
        TenantId = id,
        TenantName = id,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class RecordingObserver : ITenantLifecycleObserver
    {
        public List<string> Events { get; } = [];

        public Task OnCreatedAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            Events.Add($"created:{tenant.TenantId}");
            return Task.CompletedTask;
        }

        public Task OnUpdatedAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            Events.Add($"updated:{tenant.TenantId}");
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync(string tenantId, CancellationToken ct = default)
        {
            Events.Add($"deactivated:{tenantId}");
            return Task.CompletedTask;
        }

        public Task OnDeletedAsync(string tenantId, CancellationToken ct = default)
        {
            Events.Add($"deleted:{tenantId}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : ITenantLifecycleObserver
    {
        public Task OnCreatedAsync(TenantInfo tenant, CancellationToken ct = default)
            => throw new InvalidOperationException("the refresh is broken");

        public Task OnUpdatedAsync(TenantInfo tenant, CancellationToken ct = default)
            => throw new InvalidOperationException("the refresh is broken");

        public Task OnDeactivatedAsync(string tenantId, CancellationToken ct = default)
            => throw new InvalidOperationException("the refresh is broken");

        public Task OnDeletedAsync(string tenantId, CancellationToken ct = default)
            => throw new InvalidOperationException("the refresh is broken");
    }

    private sealed class StubTenantStore : ITenantStore
    {
        private readonly List<TenantInfo> _tenants = [];

        public bool WritesSucceed { get; init; } = true;

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Find(t => t.TenantId == tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _tenants]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                [.. _tenants.Where(t => t.State == TenantState.Active)]);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants.Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(WritesSucceed);

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(WritesSucceed);

        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(WritesSucceed);
    }
}
