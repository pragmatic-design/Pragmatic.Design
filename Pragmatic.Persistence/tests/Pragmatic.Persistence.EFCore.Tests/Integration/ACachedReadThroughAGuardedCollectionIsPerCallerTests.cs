using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization;
using Pragmatic.Caching;
using Pragmatic.Caching.Extensions;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A cached query whose root is unfiltered, and which reads a collection a permission-based filter
///     guards, is cached per caller: the rows one caller was answered with are not served to another.
/// </summary>
/// <remarks>
///     The cache key was partitioned by user only when the <b>root</b> entity had a permission-based
///     filter. Those filters reach collection navigations too, so a query reading a guarded collection
///     answered each caller differently under one key — the first reader's answer was everyone's.
/// </remarks>
public sealed class ACachedReadThroughAGuardedCollectionIsPerCallerTests : IDisposable
{
    private const string ReadAll = "product.read-all";

    private readonly TestDbContext _db;
    private readonly ServiceProvider _services;

    public ACachedReadThroughAGuardedCollectionIsPerCallerTests()
    {
        _db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"CachedInclude_{Guid.NewGuid():N}")
            .Options);

        var tools = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Tools" };
        _db.Categories.Add(tools);
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Anvil", Price = 1m, CategoryId = tools.PersistenceId });
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Rope", Price = 1m, CategoryId = tools.PersistenceId });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ACallerWithoutTheBypass_IsNotServedTheRowsOneWithItRead()
    {
        var everything = await ReadAsAsync(new Caller("admin", ReadAll));
        var guarded = await ReadAsAsync(new Caller("clerk"));

        everything.Should().ContainSingle("the bypass lifts the filter, and Tools holds a Rope");
        guarded.Should().BeEmpty("for a caller without the bypass only Anvil is in the collection");
    }

    /// <summary>
    ///     The control that makes the first test mean the cache: read first, the caller without the bypass
    ///     is filtered — so what it was served above came from the other caller's entry.
    /// </summary>
    [Fact]
    public async Task ACallerWithoutTheBypass_ReadingFirst_IsFiltered()
    {
        var guarded = await ReadAsAsync(new Caller("clerk"));

        guarded.Should().BeEmpty();
    }

    /// <summary>A projection that reads the guarded collection makes the answer the caller's too.</summary>
    [Fact]
    public async Task AProjectionThroughTheGuardedCollection_IsNotSharedEither()
    {
        var admin = await ReadCountsAsAsync(new Caller("admin", ReadAll));
        var clerk = await ReadCountsAsAsync(new Caller("clerk"));

        admin.Single().Ropes.Should().Be(1);
        clerk.Single().Ropes.Should().Be(0, "the clerk's filter hides the Rope, whoever read first");
    }

    /// <summary>
    ///     A declared join reads its set filtered for the caller, so its answer is the caller's as well.
    /// </summary>
    /// <remarks>
    ///     The filter on a joined set is applied before the join, not by the visitor, so the check that
    ///     partitions the key by what the visitor rewrites never saw it.
    /// </remarks>
    [Fact]
    public async Task AJoinThroughTheGuardedSet_IsNotSharedEither()
    {
        var admin = await ReadJoinedAsAsync(new Caller("admin", ReadAll));
        var clerk = await ReadJoinedAsAsync(new Caller("clerk"));

        admin.Select(r => r.Product).Should().BeEquivalentTo(["Anvil", "Rope"]);
        clerk.Select(r => r.Product).Should().BeEquivalentTo(["Anvil"], "the clerk's filter hides the Rope, whoever read first");
    }

    /// <summary>The control: the same caller twice shares the entry.</summary>
    [Fact]
    public async Task TheSameCaller_IsAnsweredFromTheCache()
    {
        await ReadAsAsync(new Caller("admin", ReadAll));
        _db.Products.Remove(_db.Products.Single(p => p.Name == "Rope"));
        _db.SaveChanges();

        var second = await ReadAsAsync(new Caller("admin", ReadAll));

        second.Should().ContainSingle("the second read is the cached answer, from before the Rope was removed");
    }

    private async Task<IReadOnlyList<RopeCount>> ReadCountsAsAsync(ICurrentUser caller)
        => await ExecutorFor(caller).ExecuteAllAsync(new RopeCounts(), _db.Categories.AsNoTracking())
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<JoinedRow>> ReadJoinedAsAsync(ICurrentUser caller)
        => await ExecutorFor(caller).ExecuteAllAsync(new ProductsByJoin(), _db.Categories.AsNoTracking())
            .ConfigureAwait(false);

    private EfCoreQueryExecutor ExecutorFor(ICurrentUser caller)
    {
        IQueryFilter[] filters = [new OnlyAnvils()];
        var toggle = new QueryFilterToggle();
        var provider = new DefaultQueryFilterProvider(
            filters, new PassthroughQueryFilterTypeRegistry(), toggle, currentUser: caller);
        var composer = new FilterMapComposer(toggle, [new QueryFilterProviderAdapter(filters, provider)]);
        return new EfCoreQueryExecutor(
            provider, composer, toggle, _services.GetRequiredService<ICacheStack>(), currentUser: caller,
            joinSources: new TheContextsSets(_db));
    }

    private sealed class JoinedRow
    {
        public string Product { get; init; } = "";
    }

    /// <summary>The shape a declared <c>[Join&lt;T&gt;]</c> generates, cached.</summary>
    private sealed class ProductsByJoin : IQuery<TestCategory, JoinedRow>, IJoiningQuery, ICacheable
    {
        private IQueryable<TestProduct>? _products;

        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => query;

        public void BindJoinSources(IJoinSourceProvider sources)
            => _products = sources.ForBoundary<TestCategory>().Of<TestProduct>();

        public Func<IQueryable<TestCategory>, IQueryable<JoinedRow>>? Aggregate => source => source.Join(
            _products!, c => (Guid?)c.PersistenceId, p => p.CategoryId, (c, p) => new JoinedRow { Product = p.Name });

        public string GetCacheKey() => "products-by-join";

        public CacheEntryOptions GetCacheOptions() => new() { Duration = TimeSpan.FromMinutes(5) };
    }

    private sealed class TheContextsSets(DbContext context) : IJoinSourceProvider, IJoinSources
    {
        public IJoinSources ForBoundary<TBoundary>() where TBoundary : class => this;

        public IQueryable<TEntity> Of<TEntity>() where TEntity : class, Pragmatic.Persistence.Entity.IEntity
            => context.Set<TEntity>();
    }

    private sealed class RopeCount
    {
        public int Ropes { get; init; }
    }

    private sealed class RopeCounts : IQuery<TestCategory, RopeCount>, ICacheable
    {
        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => query;

        public Expression<Func<TestCategory, RopeCount>>? Projection
            => c => new RopeCount { Ropes = c.Products.Count(p => p.Name == "Rope") };

        public string GetCacheKey() => "rope-counts";

        public CacheEntryOptions GetCacheOptions() => new() { Duration = TimeSpan.FromMinutes(5) };
    }

    private async Task<IReadOnlyList<TestCategory>> ReadAsAsync(ICurrentUser caller)
    {
        IQueryFilter[] filters = [new OnlyAnvils()];
        var toggle = new QueryFilterToggle();
        var provider = new DefaultQueryFilterProvider(
            filters, new PassthroughQueryFilterTypeRegistry(), toggle, currentUser: caller);
        var composer = new FilterMapComposer(toggle, [new QueryFilterProviderAdapter(filters, provider)]);

        var executor = new EfCoreQueryExecutor(
            provider, composer, toggle, _services.GetRequiredService<ICacheStack>(), currentUser: caller);

        return await executor.ExecuteAllAsync(new CategoriesHoldingARope(), _db.Categories.AsNoTracking())
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     The root is unfiltered, and which roots come back depends on the guarded collection it reads.
    /// </summary>
    private sealed class CategoriesHoldingARope : IQuery<TestCategory>, ICacheable
    {
        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query)
            => query.Where(c => c.Products.Any(p => p.Name == "Rope"));

        public string GetCacheKey() => "categories-holding-a-rope";

        public CacheEntryOptions GetCacheOptions() => new() { Duration = TimeSpan.FromMinutes(5) };
    }

    /// <summary>Permission-based and guarding the child only — the root is unfiltered.</summary>
    private sealed class OnlyAnvils : IPermissionBasedFilter<TestProduct>
    {
        public string BypassPermission => ReadAll;

        public Expression<Func<TestProduct, bool>> GetFilter() => p => p.Name == "Anvil";
    }

    private sealed class Caller(string id, params string[] permissions) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => id;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization { get; } = new Held(permissions);
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class Held(string[] permissions) : IUserAuthorization
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.Ordinal);

        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => _permissions;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(HasPermission);
        public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(HasPermission);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }
}
