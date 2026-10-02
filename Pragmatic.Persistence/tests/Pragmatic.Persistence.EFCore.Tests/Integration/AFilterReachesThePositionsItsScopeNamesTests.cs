using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     <see cref="FilterScope" /> decides where a filter is applied: a collection loaded with its entity
///     (<see cref="FilterScope.Collections" />), a collection read to decide which rows
///     (<see cref="FilterScope.Subqueries" />), one read to shape the result
///     (<see cref="FilterScope.Projections" />), and a set a declared join reads
///     (<see cref="FilterScope.Joins" />).
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>Subqueries</c>, <c>Projections</c> and <c>Joins</c> each have to be read by something.
///         If a navigation were filtered in every position whenever the scope held <c>Collections</c>,
///         and a joined set in none, taking a flag out would change nothing, and a filter asked for
///         everywhere would not reach a join.
///     </para>
///     <para>
///         Every case takes one flag out of <see cref="FilterScope.Default" /> and asserts both halves:
///         the position it names is left unfiltered, and the others still are. The control is
///         <see cref="FilterScope.Default" /> itself, filtered everywhere.
///     </para>
///     <para>Tools holds an Anvil and a Rope; the filter lets only the Anvil through.</para>
/// </remarks>
public sealed class AFilterReachesThePositionsItsScopeNamesTests : IAsyncLifetime
{
    private readonly TestDbContext _db = new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase($"FilterScopePositions_{Guid.NewGuid():N}")
        .Options);

    public async Task InitializeAsync()
    {
        var tools = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Tools" };
        _db.Categories.Add(tools);
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Anvil", Price = 1m, CategoryId = tools.PersistenceId });
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Rope", Price = 1m, CategoryId = tools.PersistenceId });
        await _db.SaveChangesAsync().ConfigureAwait(false);
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await _db.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task TheDefaultScope_FiltersEveryPosition()
    {
        var executor = ExecutorWith(FilterScope.Default);

        (await IncludedAsync(executor)).Should().BeEquivalentTo(["Anvil"]);
        (await WithARopeAsync(executor)).Should().BeEmpty();
        (await RopesProjectedAsync(executor)).Should().Be(0);
        (await JoinedAsync(executor)).Should().BeEquivalentTo(["Anvil"]);
    }

    [Fact]
    public async Task WithoutProjections_AProjectionReadsEveryRow_AndTheRestIsFiltered()
    {
        var executor = ExecutorWith(FilterScope.Default & ~FilterScope.Projections);

        (await RopesProjectedAsync(executor)).Should().Be(1, "a projection is the position the scope left out");
        (await IncludedAsync(executor)).Should().BeEquivalentTo(["Anvil"]);
        (await WithARopeAsync(executor)).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutSubqueries_APredicateReadsEveryRow_AndTheRestIsFiltered()
    {
        var executor = ExecutorWith(FilterScope.Default & ~FilterScope.Subqueries);

        (await WithARopeAsync(executor)).Should().BeEquivalentTo(["Tools"], "a predicate is the position the scope left out");
        (await IncludedAsync(executor)).Should().BeEquivalentTo(["Anvil"]);
        (await RopesProjectedAsync(executor)).Should().Be(0);
    }

    [Fact]
    public async Task WithoutCollections_AnIncludeLoadsEveryRow_AndTheRestIsFiltered()
    {
        var executor = ExecutorWith(FilterScope.Default & ~FilterScope.Collections);

        (await IncludedAsync(executor)).Should().BeEquivalentTo(["Anvil", "Rope"], "an include is the position the scope left out");
        (await WithARopeAsync(executor)).Should().BeEmpty();
        (await RopesProjectedAsync(executor)).Should().Be(0);
    }

    [Fact]
    public async Task WithoutJoins_AJoinedSetReadsEveryRow()
    {
        var executor = ExecutorWith(FilterScope.Default & ~FilterScope.Joins);

        (await JoinedAsync(executor)).Should().BeEquivalentTo(["Anvil", "Rope"]);
        (await IncludedAsync(executor)).Should().BeEquivalentTo(["Anvil"]);
    }

    private EfCoreQueryExecutor ExecutorWith(FilterScope scope)
    {
        IQueryFilter[] filters = [new OnlyAnvils(scope)];
        var toggle = new QueryFilterToggle();
        var caller = new Clerk();
        var provider = new DefaultQueryFilterProvider(
            filters, new PassthroughQueryFilterTypeRegistry(), toggle, currentUser: caller);
        var composer = new FilterMapComposer(toggle, [new QueryFilterProviderAdapter(filters, provider)]);
        return new EfCoreQueryExecutor(
            provider, composer, toggle, null, currentUser: caller, joinSources: new TheContextsSets(_db));
    }

    private async Task<IReadOnlyList<string>> IncludedAsync(EfCoreQueryExecutor executor)
    {
        var categories = await executor.ExecuteAllAsync(
            new Categories(q => q.Include(c => c.Products)), _db.Categories.AsNoTracking()).ConfigureAwait(false);
        return [.. categories.Single().Products.Select(p => p.Name)];
    }

    private async Task<IReadOnlyList<string>> WithARopeAsync(EfCoreQueryExecutor executor)
    {
        var categories = await executor.ExecuteAllAsync(
            new Categories(q => q.Where(c => c.Products.Any(p => p.Name == "Rope"))), _db.Categories.AsNoTracking())
            .ConfigureAwait(false);
        return [.. categories.Select(c => c.Name)];
    }

    private async Task<int> RopesProjectedAsync(EfCoreQueryExecutor executor)
        => (await executor.ExecuteAllAsync(new RopeCounts(), _db.Categories.AsNoTracking()).ConfigureAwait(false))
            .Single().Ropes;

    private async Task<IReadOnlyList<string>> JoinedAsync(EfCoreQueryExecutor executor)
    {
        var rows = await executor.ExecuteAllAsync(new ProductsByJoin(), _db.Categories.AsNoTracking())
            .ConfigureAwait(false);
        return [.. rows.Select(r => r.Product)];
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

    private sealed class JoinedRow
    {
        public string Product { get; init; } = "";
    }

    /// <summary>
    ///     The shape a declared <c>[Join&lt;T&gt;]</c> generates: the set bound by the executor, joined
    ///     by key.
    /// </summary>
    private sealed class ProductsByJoin : IQuery<TestCategory, JoinedRow>, IJoiningQuery
    {
        private IQueryable<TestProduct>? _products;

        public IQueryable<TestCategory> Apply(IQueryable<TestCategory> query) => query;

        public void BindJoinSources(IJoinSourceProvider sources)
            => _products = sources.ForBoundary<TestCategory>().Of<TestProduct>();

        public Func<IQueryable<TestCategory>, IQueryable<JoinedRow>>? Aggregate => source => source.Join(
            _products!, c => (Guid?)c.PersistenceId, p => p.CategoryId, (c, p) => new JoinedRow { Product = p.Name });
    }

    /// <summary>The sets of the one context the test has, whatever boundary is named.</summary>
    private sealed class TheContextsSets(DbContext context) : IJoinSourceProvider, IJoinSources
    {
        public IJoinSources ForBoundary<TBoundary>() where TBoundary : class => this;

        public IQueryable<TEntity> Of<TEntity>() where TEntity : class, Pragmatic.Persistence.Entity.IEntity
            => context.Set<TEntity>();
    }

    private sealed class OnlyAnvils(FilterScope scope) : IPermissionBasedFilter<TestProduct>
    {
        public string BypassPermission => "product.read-all";

        public FilterScope Scope => scope;

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
