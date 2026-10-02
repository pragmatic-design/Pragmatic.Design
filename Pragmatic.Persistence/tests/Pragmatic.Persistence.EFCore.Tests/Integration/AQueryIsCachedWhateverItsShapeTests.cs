using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Caching.Extensions;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A <c>[Cacheable]</c> query is cached whatever its shape: a list and a single row, not only
///     a page.
/// </summary>
/// <remarks>
///     <para>
///         Only the paged <c>ExecuteAsync</c> asked the query whether it was <see cref="ICacheable" />; the
///         list and single overloads read the database every time. The generator writes the cache key for
///         every <c>[Query]</c> with <c>[Cacheable]</c>, and the invoker picks the overload by the query's
///         shape — so a non-paged query compiled, declared its cache, and never used it. Found in the
///         Warehouse example, whose availability read is a list.
///     </para>
///     <para>
///         Measured the only way a cache can be: the database changes between two reads, and the second
///         read answers what the first one saw.
///     </para>
/// </remarks>
public sealed class AQueryIsCachedWhateverItsShapeTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly ServiceProvider _services;
    private readonly EfCoreQueryExecutor _executor;

    public AQueryIsCachedWhateverItsShapeTests()
    {
        _db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"Cached_{Guid.NewGuid():N}")
            .Options);
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Anvil", Price = 12.5m });
        _db.SaveChanges();

        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        _services = services.BuildServiceProvider();

        _executor = new EfCoreQueryExecutor(null, _services.GetRequiredService<ICacheStack>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AListQuery_IsReadOnce_ThenAnsweredFromTheCache()
    {
        var first = await _executor.ExecuteAllAsync(new CachedNames(), _db.Products);
        AddProduct("Rope");

        var second = await _executor.ExecuteAllAsync(new CachedNames(), _db.Products);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1, "the second read is the cached answer, before Rope was added");
    }

    [Fact]
    public async Task AnEntityListQuery_IsReadOnce_ThenAnsweredFromTheCache()
    {
        var first = await _executor.ExecuteAllAsync(new CachedProducts(), _db.Products);
        AddProduct("Rope");

        var second = await _executor.ExecuteAllAsync(new CachedProducts(), _db.Products);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1, "the second read is the cached answer, before Rope was added");
    }

    [Fact]
    public async Task ASingleQuery_IsReadOnce_ThenAnsweredFromTheCache()
    {
        var first = await _executor.ExecuteSingleAsync(new CachedProductNamed("Anvil"), _db.Products);
        RenameAnvil("Hammer");

        var second = await _executor.ExecuteSingleAsync(new CachedProductNamed("Anvil"), _db.Products);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("the cached row, found before the rename");
    }

    /// <summary>
    ///     The control: a single query that finds nothing is not cached — a row created afterwards is found
    ///     by the next read, not hidden behind a cached "not found".
    /// </summary>
    [Fact]
    public async Task ASingleQueryThatFindsNothing_IsNotCached()
    {
        var missing = await _executor.ExecuteSingleAsync(new CachedProductNamed("Rope"), _db.Products);
        AddProduct("Rope");

        var found = await _executor.ExecuteSingleAsync(new CachedProductNamed("Rope"), _db.Products);

        missing.IsFailure.Should().BeTrue();
        found.IsSuccess.Should().BeTrue("a failure is never cached");
    }

    /// <summary>The control: once the tag is dropped, the next read goes to the database again.</summary>
    [Fact]
    public async Task AfterItsTagIsInvalidated_TheListIsReadAgain()
    {
        await _executor.ExecuteAllAsync(new CachedNames(), _db.Products);
        AddProduct("Rope");

        await _services.GetRequiredService<ICacheStack>().InvalidateByTagAsync("products");
        var reread = await _executor.ExecuteAllAsync(new CachedNames(), _db.Products);

        reread.Should().HaveCount(2);
    }

    private void AddProduct(string name)
    {
        _db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = name, Price = 1m });
        _db.SaveChanges();
    }

    private void RenameAnvil(string name)
    {
        _db.Products.Single(p => p.Name == "Anvil").Name = name;
        _db.SaveChanges();
    }

    private static readonly CacheEntryOptions Options = new() { Duration = TimeSpan.FromMinutes(5), Tags = ["products"] };

    /// <summary>A projected list, the shape the Warehouse availability read has.</summary>
    private sealed class CachedNames : IQuery<TestProduct, ProductName>, ICacheable
    {
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;

        public System.Linq.Expressions.Expression<Func<TestProduct, ProductName>>? Projection
            => p => new ProductName { Name = p.Name };

        public string GetCacheKey() => "names";

        public CacheEntryOptions GetCacheOptions() => Options;
    }

    private sealed class CachedProducts : IQuery<TestProduct>, ICacheable
    {
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;

        public string GetCacheKey() => "products";

        public CacheEntryOptions GetCacheOptions() => Options;
    }

    private sealed class CachedProductNamed(string name) : IQuery<TestProduct>, ICacheable
    {
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query.Where(p => p.Name == name);

        public string GetCacheKey() => $"named:{name}";

        public CacheEntryOptions GetCacheOptions() => Options;
    }

    private sealed class ProductName
    {
        public string Name { get; init; } = "";
    }
}
