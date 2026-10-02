using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A collection guarded by a permission-based filter is filtered whatever reads it — an
///     <c>Include</c>, a predicate, a projection, an aggregate — and the query keeps compiling.
/// </summary>
/// <remarks>
///     <para>
///         The navigation visitor replaced <c>c.Products</c> with <c>c.Products.Where(filter)</c>, an
///         <c>IEnumerable</c> where the model declared an <c>ICollection</c>. Anything that needed the
///         declared type broke: an <c>Include</c> lambda threw, and so did a read of <c>Count</c>.
///     </para>
///     <para>
///         And it never saw a projection or an aggregate, which are applied after the filters: a
///         collection read inside one was not filtered at all.
///     </para>
///     <para>
///         The caller here holds no bypass, so only Anvil passes the filter; Tools holds an Anvil and a
///         Rope.
///     </para>
///     <para>
///         Run on EF's in-memory provider and on PostgreSQL: the rewrite has to be something a real
///         provider translates, not only something the in-memory one evaluates.
///     </para>
/// </remarks>
public abstract class AGuardedCollectionIsFilteredWhateverReadsIt : IAsyncLifetime
{
    private TestDbContext _db = null!;
    private EfCoreQueryExecutor _executor = null!;
    private FilterMapComposer _composer = null!;

    /// <summary>A context on an empty database of this test's own.</summary>
    protected abstract TestDbContext CreateContext();

    public async Task InitializeAsync()
    {
        _db = CreateContext();
        await _db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        var tools = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Tools" };
        _db.Categories.Add(tools);
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Anvil", Price = 1m, CategoryId = tools.PersistenceId });
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Rope", Price = 1m, CategoryId = tools.PersistenceId });
        await _db.SaveChangesAsync().ConfigureAwait(false);
        _db.ChangeTracker.Clear();

        IQueryFilter[] filters = [new OnlyAnvils()];
        var toggle = new QueryFilterToggle();
        var caller = new Clerk();
        var provider = new DefaultQueryFilterProvider(
            filters, new PassthroughQueryFilterTypeRegistry(), toggle, currentUser: caller);
        _composer = new FilterMapComposer(toggle, [new QueryFilterProviderAdapter(filters, provider)]);
        _executor = new EfCoreQueryExecutor(provider, _composer, toggle, null, currentUser: caller);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await _db.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AnInclude_IsFiltered()
    {
        var tools = await _executor.ExecuteAllAsync(
            new Categories(q => q.Include(c => c.Products)), _db.Categories.AsNoTracking());

        tools.Single().Products.Select(p => p.Name).Should().BeEquivalentTo(["Anvil"]);
    }

    /// <summary>
    ///     A <c>ThenInclude</c> after a filtered <c>Include</c> keeps its chain: the include becomes EF's
    ///     filtered include, and the <c>ThenInclude</c> reads from it.
    /// </summary>
    /// <remarks>
    ///     Asserted on the rewritten expression: the test model has no collection whose elements lead
    ///     further on, and EF refuses a <c>ThenInclude</c> that walks back to the root.
    /// </remarks>
    [Fact]
    public void AnIncludeThenInclude_KeepsItsChain()
    {
        var query = _db.Categories.Include(c => c.Products).ThenInclude(p => p.Category);

        var rewritten = (MethodCallExpression)_composer.ApplyVisitor(query.Expression, FilterContext.At(DateTimeOffset.UnixEpoch));

        rewritten.Method.Name.Should().Be("ThenInclude");
        var include = (MethodCallExpression)rewritten.Arguments[0];
        include.Method.Name.Should().Be("Include");
        include.Type.Should().Be(typeof(IIncludableQueryable<TestCategory, IEnumerable<TestProduct>>));
        include.ToString().Should().Contain("Where");
    }

    [Fact]
    public async Task ACountProperty_ReadsTheFilteredCollection()
    {
        var withTwo = await _executor.ExecuteAllAsync(
            new Categories(q => q.Where(c => c.Products.Count > 1)), _db.Categories.AsNoTracking());

        withTwo.Should().BeEmpty("the caller sees one product in Tools, not two");
    }

    [Fact]
    public async Task AProjection_ReadsTheFilteredCollection()
    {
        var counts = await _executor.ExecuteAllAsync(new RopeCounts(), _db.Categories.AsNoTracking());

        counts.Single().Ropes.Should().Be(0);
    }

    [Fact]
    public async Task AnAggregate_ReadsTheFilteredCollection()
    {
        var counts = await _executor.ExecuteAllAsync(new RopeCountsAggregated(), _db.Categories.AsNoTracking());

        counts.Single().Ropes.Should().Be(0);
    }

    /// <summary>The control: a predicate that only enumerated was already filtered.</summary>
    [Fact]
    public async Task AnAny_IsFiltered()
    {
        var withRope = await _executor.ExecuteAllAsync(
            new Categories(q => q.Where(c => c.Products.Any(p => p.Name == "Rope"))), _db.Categories.AsNoTracking());

        withRope.Should().BeEmpty();
    }

    private sealed class Categories(Func<IQueryable<TestCategory>, IQueryable<TestCategory>> apply) : IQuery<TestCategory>
    {
        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => apply(query);
    }

    private sealed class RopeCount
    {
        public int Ropes { get; init; }
    }

    private sealed class RopeCounts : IQuery<TestCategory, RopeCount>
    {
        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => query;

        public Expression<Func<TestCategory, RopeCount>>? Projection
            => c => new RopeCount { Ropes = c.Products.Count(p => p.Name == "Rope") };
    }

    private sealed class RopeCountsAggregated : IQuery<TestCategory, RopeCount>
    {
        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => query;

        public Func<IQueryable<TestCategory>, IQueryable<RopeCount>>? Aggregate
            => q => q.Select(c => new RopeCount { Ropes = c.Products.Count(p => p.Name == "Rope") });
    }

    private sealed class OnlyAnvils : IPermissionBasedFilter<TestProduct>
    {
        public string BypassPermission => "product.read-all";

        public Expression<Func<TestProduct, bool>> GetFilter() => p => p.Name == "Anvil";
    }

    private sealed class Clerk : ICurrentUser
    {
        public string Id => "clerk";
        public string? DisplayName => "Clerk";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }
}
