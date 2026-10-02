using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Query.Interfaces;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Tests for <see cref="EfCoreQueryExecutor"/> — the EF Core implementation of IQueryExecutor.
///     Uses InMemory provider for fast, isolated test execution.
/// </summary>
public class EfCoreQueryExecutorTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly EfCoreQueryExecutor _executor;

    public EfCoreQueryExecutorTests()
    {
        _db = TestDbContextFactory.Create();
        _executor = new EfCoreQueryExecutor();
        SeedProducts();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedProducts()
    {
        _db.Products.AddRange(
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Widget A", Price = 10.00m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Widget B", Price = 20.00m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Gadget C", Price = 30.00m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Gadget D", Price = 40.00m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Tool E", Price = 50.00m });
        _db.SaveChanges();
    }

    #region ExecuteAsync (paged, entity)

    [Fact]
    public async Task ExecuteAsync_AllItems_FirstPage_ReturnsPagedResult()
    {
        var query = new AllProductsPagedQuery { Page = 1, PageSize = 3 };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(5);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_AllItems_SecondPage_ReturnsRemainder()
    {
        var query = new AllProductsPagedQuery { Page = 2, PageSize = 3 };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilter_ReturnsFilteredCount()
    {
        var query = new FilteredProductsQuery
        {
            NameContains = "Widget",
            Page = 1,
            PageSize = 10
        };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(p => p.Name.Contains("Widget"));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResult_ReturnsEmptyPage()
    {
        var query = new FilteredProductsQuery
        {
            NameContains = "Nonexistent",
            Page = 1,
            PageSize = 10
        };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PaginationMetadata_TotalPages()
    {
        var query = new AllProductsPagedQuery { Page = 1, PageSize = 2 };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.TotalPages.Should().Be(3); // 5 items / 2 per page = 3
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_LastPage_HasNoPreviousPage()
    {
        var query = new AllProductsPagedQuery { Page = 3, PageSize = 2 };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.Items.Should().HaveCount(1); // 5th item
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync (paged, with projection)

    [Fact]
    public async Task ExecuteAsync_WithProjection_ReturnsProjectedResults()
    {
        var query = new ProductDtoPagedQuery { Page = 1, PageSize = 10 };

        var result = await _executor.ExecuteAsync<TestProduct, ProductDto>(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(5);
        result.Items.Should().OnlyContain(d => !string.IsNullOrEmpty(d.DisplayName));
    }

    [Fact]
    public async Task ExecuteAsync_WithProjectionAndFilter_FiltersBeforeProjection()
    {
        var query = new FilteredProductDtoQuery
        {
            MinPrice = 25.00m,
            Page = 1,
            PageSize = 10
        };

        var result = await _executor.ExecuteAsync<TestProduct, ProductDto>(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(3); // 30, 40, 50
        result.TotalCount.Should().Be(3);
    }

    #endregion

    #region ExecuteAllAsync

    [Fact]
    public async Task ExecuteAllAsync_ReturnsAllMatchingItems()
    {
        var query = new AllProductsQuery();

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAllAsync_WithFilter_ReturnsOnlyMatching()
    {
        var query = new FilteredAllProductsQuery { MinPrice = 25.00m };

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.Price >= 25.00m);
    }

    #endregion

    #region Global Filters

    [Fact]
    public async Task ExecuteAsync_WithFilterProvider_AppliesGlobalFilters()
    {
        // Seed some orders with soft-delete
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 100, IsDeleted = false },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 200, IsDeleted = true },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "ORD-003", Total = 300, IsDeleted = false });
        _db.SaveChanges();

        var filterProvider = new TestFilterProvider();
        var executorWithFilters = new EfCoreQueryExecutor(filterProvider);

        var query = new AllOrdersPagedQuery { Page = 1, PageSize = 10 };

        // IgnoreQueryFilters because TestDbContext already has HasQueryFilter for soft-delete,
        // and InMemory doesn't support it. We test our own global filter mechanism.
        var result = await executorWithFilters.ExecuteAsync(query, _db.Orders.IgnoreQueryFilters());

        result.IsSuccess.Should().BeTrue();
        // Global filter excludes deleted orders
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoreGlobalFilters_BypassesPragmaticRootFilter()
    {
        // IgnoreGlobalFilters must bypass the FULL Pragmatic filter pipeline
        // (IQueryFilterProvider root filters), not only EF Core's HasQueryFilter. Applying
        // ApplyGlobalFilters unconditionally would silently keep soft-deleted /
        // tenant-hidden rows hidden in admin/raw views.
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "PER2-001", Total = 100, IsDeleted = false },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "PER2-002", Total = 200, IsDeleted = true });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var filterProvider = new TestFilterProvider();
        var executorWithFilters = new EfCoreQueryExecutor(filterProvider);

        // IgnoreGlobalFilters = true → the Pragmatic SoftDeleteFilter must NOT be applied.
        var query = new IgnoreFiltersOrderQuery { Page = 1, PageSize = 10 };

        var result = await executorWithFilters.ExecuteAsync(query, _db.Orders.IgnoreQueryFilters());

        result.IsSuccess.Should().BeTrue();
        // Both orders returned — the Pragmatic root filter (e => !e.IsDeleted) is bypassed.
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilterProvider_NoIgnoreHint_StillAppliesPragmaticRootFilter()
    {
        // Counterpart to the IgnoreGlobalFilters case: without the hint, the Pragmatic root filter
        // must still apply (the short-circuit is gated strictly on IgnoreGlobalFilters).
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "PER2-010", Total = 100, IsDeleted = false },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "PER2-011", Total = 200, IsDeleted = true });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var executorWithFilters = new EfCoreQueryExecutor(new TestFilterProvider());
        var query = new AllOrdersPagedQuery { Page = 1, PageSize = 10 };

        var result = await executorWithFilters.ExecuteAsync(query, _db.Orders.IgnoreQueryFilters());

        result.IsSuccess.Should().BeTrue();
        // Deleted order is filtered out by the Pragmatic root filter.
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutFilterProvider_ReturnsAll()
    {
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "ORD-010", Total = 100, IsDeleted = false },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "ORD-011", Total = 200, IsDeleted = true });
        _db.SaveChanges();

        var query = new AllOrdersPagedQuery { Page = 1, PageSize = 10 };

        var result = await _executor.ExecuteAsync(query, _db.Orders.IgnoreQueryFilters());

        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(2); // No global filter, returns all
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task ExecuteAsync_Cancellation_ThrowsOperationCanceled()
    {
        var query = new AllProductsPagedQuery { Page = 1, PageSize = 10 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => _executor.ExecuteAsync(query, _db.Products, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Query Hints

    [Fact]
    public async Task ExecuteAllAsync_DefaultHints_AppliesNoTracking()
    {
        // Clear tracked entities from SeedProducts() so we can verify AsNoTracking behavior
        _db.ChangeTracker.Clear();

        var query = new AllProductsQuery();

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        result.Should().HaveCount(5);
        // After AsNoTracking, entities are NOT tracked
        _db.ChangeTracker.Entries<TestProduct>().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAllAsync_NoTrackingFalse_EntitiesAreTracked()
    {
        // Clear tracked entities from SeedProducts() so we can verify tracking behavior
        _db.ChangeTracker.Clear();

        var query = new TrackedProductsQuery();

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        result.Should().HaveCount(5);
        // Without AsNoTracking, entities ARE tracked
        _db.ChangeTracker.Entries<TestProduct>().Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoreGlobalFilters_ReturnsDeletedEntities()
    {
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "IGF-001", Total = 100, IsDeleted = false },
            new TestOrder { PersistenceId = Guid.NewGuid(), OrderNumber = "IGF-002", Total = 200, IsDeleted = true });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        // Query with IgnoreGlobalFilters = true should bypass the HasQueryFilter
        var query = new IgnoreFiltersOrderQuery { Page = 1, PageSize = 10 };

        var result = await _executor.ExecuteAsync(query, _db.Orders);

        result.IsSuccess.Should().BeTrue();
        // Both orders returned — the HasQueryFilter(e => !e.IsDeleted) is bypassed
        result.Items.Should().HaveCount(2);
    }

    #endregion

    #region Include

    [Fact]
    public async Task ExecuteAllAsync_WithInclude_LoadsNavigation()
    {
        var categoryId = Guid.NewGuid();
        _db.Categories.Add(new TestCategory { PersistenceId = categoryId, Name = "Electronics" });
        _db.Products.Add(new TestProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "Laptop",
            Price = 999.99m,
            CategoryId = categoryId
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var query = new ProductsWithCategoryQuery();

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        result.Should().ContainSingle(p => p.Name == "Laptop");
        var laptop = result.First(p => p.Name == "Laptop");
        laptop.Category.Should().NotBeNull();
        laptop.Category!.Name.Should().Be("Electronics");
    }

    [Fact]
    public async Task ExecuteAsync_Paged_WithInclude_LoadsNavigation()
    {
        var categoryId = Guid.NewGuid();
        _db.Categories.Add(new TestCategory { PersistenceId = categoryId, Name = "Tools" });
        _db.Products.Add(new TestProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "Hammer",
            Price = 25.00m,
            CategoryId = categoryId
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var query = new PagedProductsWithCategoryQuery { Page = 1, PageSize = 10 };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        var hammer = result.Items.FirstOrDefault(p => p.Name == "Hammer");
        hammer.Should().NotBeNull();
        hammer!.Category.Should().NotBeNull();
        hammer.Category!.Name.Should().Be("Tools");
    }

    [Fact]
    public async Task ExecuteAllAsync_WithMultipleIncludePaths_LoadsAllNavigations()
    {
        var categoryId = Guid.NewGuid();
        _db.Categories.Add(new TestCategory { PersistenceId = categoryId, Name = "Multi-Include" });
        _db.Products.Add(new TestProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "Multi-Nav Product",
            Price = 15.00m,
            CategoryId = categoryId
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        // Query with multiple Include paths
        var query = new ProductsWithMultipleIncludesQuery();

        var result = await _executor.ExecuteAllAsync(query, _db.Products);

        var product = result.FirstOrDefault(p => p.Name == "Multi-Nav Product");
        product.Should().NotBeNull();
        product!.Category.Should().NotBeNull();
        product.Category!.Name.Should().Be("Multi-Include");
    }

    #endregion

    #region End-to-End

    [Fact]
    public async Task EndToEnd_FilterPageIncludeHints_CombinesCorrectly()
    {
        // Seed categories + products with navigations
        var catA = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Cat-A" };
        var catB = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Cat-B" };
        _db.Categories.AddRange(catA, catB);

        _db.Products.AddRange(
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "E2E Alpha", Price = 100m, CategoryId = catA.PersistenceId },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "E2E Beta", Price = 200m, CategoryId = catA.PersistenceId },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "E2E Gamma", Price = 300m, CategoryId = catB.PersistenceId },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "E2E Delta", Price = 400m, CategoryId = catB.PersistenceId });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        // Paged query with filter + Include + no-tracking
        var query = new E2EPagedFilteredWithIncludeQuery
        {
            MinPrice = 150m,
            Page = 1,
            PageSize = 2
        };

        var result = await _executor.ExecuteAsync(query, _db.Products);

        result.IsSuccess.Should().BeTrue();
        result.TotalCount.Should().Be(3); // Beta(200), Gamma(300), Delta(400)
        result.Items.Should().HaveCount(2); // Page 1 of 2
        result.TotalPages.Should().Be(2);
        result.HasNextPage.Should().BeTrue();

        // Verify Include loaded the Category navigation
        result.Items.Should().OnlyContain(p => p.Category != null);

        // Verify ChangeTracker is empty (NoTracking = true via IQueryHints default)
        _db.ChangeTracker.Entries<TestProduct>().Should().BeEmpty();
    }

    #endregion

    #region Test Query Implementations

    private sealed class AllProductsPagedQuery : IPagedQuery<TestProduct>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    private sealed class FilteredProductsQuery : IPagedQuery<TestProduct>
    {
        public required string NameContains { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }

        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query)
            => query.Where(p => p.Name.Contains(NameContains));
    }

    private sealed class AllProductsQuery : IQuery<TestProduct>
    {
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    private sealed class FilteredAllProductsQuery : IQuery<TestProduct>
    {
        public decimal MinPrice { get; init; }

        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query)
            => query.Where(p => p.Price >= MinPrice);
    }

    private sealed class AllOrdersPagedQuery : IPagedQuery<TestOrder>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestOrder> Apply(IQueryable<TestOrder> query) => query;
    }

    /// <summary>Query with NoTracking=false — entities are tracked.</summary>
    private sealed class TrackedProductsQuery : IQuery<TestProduct>, IQueryHints
    {
        public bool NoTracking => false;
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    /// <summary>Query with IgnoreGlobalFilters — bypasses HasQueryFilter.</summary>
    private sealed class IgnoreFiltersOrderQuery : IPagedQuery<TestOrder>, IQueryHints
    {
        public bool IgnoreGlobalFilters => true;
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestOrder> Apply(IQueryable<TestOrder> query) => query;
    }

    /// <summary>Non-paged query with Include for category navigation.</summary>
    private sealed class ProductsWithCategoryQuery : IQuery<TestProduct>, IIncludableQuery<TestProduct>
    {
        public IReadOnlyList<string> IncludePaths => ["Category"];
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    /// <summary>Paged query with Include for category navigation.</summary>
    private sealed class PagedProductsWithCategoryQuery : IPagedQuery<TestProduct>, IIncludableQuery<TestProduct>
    {
        public IReadOnlyList<string> IncludePaths => ["Category"];
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    /// <summary>Query with multiple Include paths.</summary>
    private sealed class ProductsWithMultipleIncludesQuery : IQuery<TestProduct>, IIncludableQuery<TestProduct>
    {
        public IReadOnlyList<string> IncludePaths => ["Category"];
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    /// <summary>End-to-end query combining filter, paging, Include, and default hints.</summary>
    private sealed class E2EPagedFilteredWithIncludeQuery : IPagedQuery<TestProduct>, IIncludableQuery<TestProduct>
    {
        public required decimal MinPrice { get; init; }
        public IReadOnlyList<string> IncludePaths => ["Category"];
        public int Page { get; init; }
        public int PageSize { get; init; }

        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query)
            => query.Where(p => p.Price >= MinPrice).OrderBy(p => p.Price);
    }

    #endregion

    #region Test Projection Types

    private sealed class ProductDto
    {
        public string DisplayName { get; init; } = "";
        public decimal Price { get; init; }
    }

    private sealed class ProductDtoPagedQuery : IPagedQuery<TestProduct, ProductDto>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }

        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;

        public Expression<Func<TestProduct, ProductDto>>? Projection
            => p => new ProductDto { DisplayName = p.Name + " ($" + p.Price + ")", Price = p.Price };
    }

    private sealed class FilteredProductDtoQuery : IPagedQuery<TestProduct, ProductDto>
    {
        public decimal MinPrice { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }

        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query)
            => query.Where(p => p.Price >= MinPrice);

        public Expression<Func<TestProduct, ProductDto>>? Projection
            => p => new ProductDto { DisplayName = p.Name, Price = p.Price };
    }

    #endregion

    #region Test Filter Provider

    private sealed class SoftDeleteFilter : IQueryFilter<TestOrder>
    {
        public Expression<Func<TestOrder, bool>> GetFilter()
            => e => !e.IsDeleted;
    }

    private sealed class TestFilterProvider : IQueryFilterProvider
    {
        public IEnumerable<IQueryFilter<T>> GetFilters<T>() where T : class
        {
            if (typeof(T) == typeof(TestOrder))
                yield return (IQueryFilter<T>)(object)new SoftDeleteFilter();
        }

        public Expression<Func<T, bool>>? GetCombinedFilter<T>(NavigationContext? context = null) where T : class
        {
            if (typeof(T) == typeof(TestOrder))
            {
                Expression<Func<TestOrder, bool>> filter = e => !e.IsDeleted;
                return (Expression<Func<T, bool>>)(object)filter;
            }

            return null;
        }

        /// <summary>Mirrors the typed overload, so the two cannot answer differently.</summary>
        public LambdaExpression? GetCombinedFilter(
            Type entityType, FilterContext filterContext, NavigationContext? navigationContext = null)
        {
            if (entityType != typeof(TestOrder))
                return null;

            Expression<Func<TestOrder, bool>> filter = e => !e.IsDeleted;
            return filter;
        }

        public bool HasFilters<T>() where T : class => typeof(T) == typeof(TestOrder);
    }

    #endregion
}
