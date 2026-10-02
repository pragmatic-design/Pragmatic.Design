using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Repository;

/// <summary>
///     The first consumer of the tenant lifecycle signal: the lookup caches follow the tenants.
/// </summary>
/// <remarks>
///     <para>
///         A tenant-scoped <c>[Lookup]</c> is preloaded once per active tenant at startup. Without the
///         signal, a tenant created after that would have no cache, and every navigation over that
///         lookup would throw for it until the host restarted — nothing else tells the preload that the
///         set of tenants has changed.
///     </para>
///     <para>
///         ⚠️ No database here on purpose: the loader is the seam being tested, not what it reads. What
///         has to be true is that the observer asks the loaders for the new tenant and drops what was
///         derived for a tenant that went away — a real load needs a generated loader and a schema, and
///         it is covered where those exist.
///     </para>
/// </remarks>
[Collection(LookupResolverCollection.Name)]
public sealed class LookupCacheTenantObserverTests : IDisposable
{
    public LookupCacheTenantObserverTests() => LookupResolver.Reset();

    public void Dispose()
    {
        LookupResolver.Reset();
        GC.SuppressFinalize(this);
    }

    /// <summary>A tenant created while the host runs gets its lookup caches loaded.</summary>
    [Fact]
    public async Task ATenantCreatedWhileTheHostRuns_GetsItsCachesLoaded()
    {
        var loader = new RecordingLoader();
        var observer = Observer(loader);

        await observer.OnCreatedAsync(Tenant("tenant-a", TenantState.Active));

        loader.LoadedFor.Should().BeEquivalentTo(["tenant-a"]);
    }

    /// <summary>The control: a tenant that arrives already deactivated is not loaded.</summary>
    /// <remarks>
    ///     Without it, "reacts to a create" is satisfied by loading for every tenant that is mentioned,
    ///     and the preload at startup only loads the active ones — the two would disagree.
    /// </remarks>
    [Fact]
    public async Task ATenantThatArrivesDeactivated_IsNotLoaded()
    {
        var loader = new RecordingLoader();
        var observer = Observer(loader);

        await observer.OnCreatedAsync(Tenant("tenant-a", TenantState.Deactivated));

        loader.LoadedFor.Should().BeEmpty();
    }

    /// <summary>Deactivating a tenant drops what was derived for it.</summary>
    /// <remarks>
    ///     The reverse half of the claim, and the one that says the signal is not just a warm-up hook:
    ///     a cache that outlives its tenant serves rows to whoever gets that tenant id next.
    /// </remarks>
    [Fact]
    public async Task ATenantDeactivated_LosesItsCaches()
    {
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));

        await Observer(new RecordingLoader()).OnDeactivatedAsync("tenant-a");

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.TryGet<Status, int>(1, out _).Should().BeFalse();
    }

    /// <summary>And so does deleting one.</summary>
    [Fact]
    public async Task ATenantDeleted_LosesItsCaches()
    {
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));

        await Observer(new RecordingLoader()).OnDeletedAsync("tenant-a");

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.TryGet<Status, int>(1, out _).Should().BeFalse();
    }

    /// <summary>
    ///     The control for both: another tenant keeps its caches, and the shared ones are untouched.
    /// </summary>
    /// <remarks>
    ///     Without it, "drops what was derived for it" is satisfied by <c>LookupResolver.Reset()</c> —
    ///     one tenant leaving would empty every other tenant's cache and the shared slot with them,
    ///     which is a worse outage than the staleness this fixes.
    /// </remarks>
    [Fact]
    public async Task DroppingOneTenant_LeavesTheOthersAndTheSharedSlotAlone()
    {
        LookupResolver.Register<Status, int>(new StubCache(new Status("shared")));
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));
        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only B")));

        await Observer(new RecordingLoader()).OnDeactivatedAsync("tenant-a");

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("only B");

        LookupResolver.Get<Status, int>(1).Name.Should().Be("shared");

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Get<Status, int>(1).Name.Should()
                .Be("shared", "the tenant's own rows are gone, and the shared fallback is what remains");
    }

    /// <summary>An update that reactivates a tenant loads its caches again.</summary>
    /// <remarks>
    ///     <c>UpdateAsync</c> is how a deactivated tenant comes back: the state travels on the tenant,
    ///     so the observer reads it rather than assuming an update means "still active".
    /// </remarks>
    [Fact]
    public async Task AnUpdateThatReactivatesATenant_LoadsItsCachesAgain()
    {
        var loader = new RecordingLoader();
        var observer = Observer(loader);

        await observer.OnUpdatedAsync(Tenant("tenant-a", TenantState.Active));

        loader.LoadedFor.Should().BeEquivalentTo(["tenant-a"]);
    }

    /// <summary>And an update that deactivates one drops its caches instead of loading them.</summary>
    [Fact]
    public async Task AnUpdateThatDeactivatesATenant_DropsItsCaches()
    {
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));
        var loader = new RecordingLoader();

        await Observer(loader).OnUpdatedAsync(Tenant("tenant-a", TenantState.Deactivated));

        loader.LoadedFor.Should().BeEmpty();
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.TryGet<Status, int>(1, out _).Should().BeFalse();
    }

    /// <summary>A loader that throws does not stop the others, and does not fail the transition.</summary>
    /// <remarks>
    ///     Unlike the startup preload, which refuses to start rather than run with an incomplete cache:
    ///     here the tenant has already been created, and throwing back at the caller would report a
    ///     create that happened as one that did not.
    /// </remarks>
    [Fact]
    public async Task ALoaderThatThrows_DoesNotStopTheOthers()
    {
        var loader = new RecordingLoader();
        var observer = new LookupCacheTenantObserver(
            new ServiceCollection().BuildServiceProvider(), [new ThrowingLoader(), loader]);

        await observer.OnCreatedAsync(Tenant("tenant-a", TenantState.Active));

        loader.LoadedFor.Should().BeEquivalentTo(["tenant-a"]);
    }

    private static LookupCacheTenantObserver Observer(params ILookupCacheLoader[] loaders)
        => new(new ServiceCollection().BuildServiceProvider(), loaders);

    private static TenantInfo Tenant(string id, TenantState state) => new()
    {
        TenantId = id,
        TenantName = id,
        State = state,
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private sealed record Status(string Name);

    private sealed class StubCache(Status only) : ILookupCache<Status, int>
    {
        public Status Get(int id) => only;

        public bool TryGet(int id, out Status? value)
        {
            value = only;
            return true;
        }

        public IReadOnlyList<Status> GetAll() => [only];
    }

    private sealed class RecordingLoader : ILookupCacheLoader
    {
        public List<string> LoadedFor { get; } = [];

        public Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct)
            => Task.CompletedTask;

        public Task LoadForTenantAsync(IServiceProvider serviceProvider, string tenantId, CancellationToken ct)
        {
            LoadedFor.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingLoader : ILookupCacheLoader
    {
        public Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct)
            => Task.CompletedTask;

        public Task LoadForTenantAsync(IServiceProvider serviceProvider, string tenantId, CancellationToken ct)
            => throw new InvalidOperationException("no database here");
    }
}
