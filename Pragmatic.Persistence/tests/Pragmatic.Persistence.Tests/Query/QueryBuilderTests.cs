using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Builder;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.QueryTests;

/// <summary>
///     Tests for QueryBuilder fluent API.
/// </summary>
public class QueryBuilderTests
{
    #region Test Entities

    private class Order
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public decimal Total { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CustomerId { get; set; }
        public Customer? Customer { get; set; }
        public List<OrderLine> Lines { get; set; } = [];
    }

    private class Customer
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class OrderLine
    {
        public Guid Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
    }

    private enum OrderStatus { Pending, Shipped, Delivered, Cancelled }

    #endregion

    #region Query.For<T>() Entry Point

    [Fact]
    public void For_CreatesEmptyQueryBuilder()
    {
        // Act
        var builder = PersistenceQuery.For<Order>();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<QueryBuilder<Order>>();
    }

    [Fact]
    public void For_BuildWithoutModifications_ReturnsOriginalQuery()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>().Build(orders).ToList();

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region WithFilter Tests

    [Fact]
    public void WithFilter_SingleCondition_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.Status == OrderStatus.Shipped)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.Status.Should().Be(OrderStatus.Shipped));
    }

    [Fact]
    public void WithFilter_MultipleConditions_ChainsWithAnd()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.Status == OrderStatus.Shipped)
            .WithFilter(o => o.Total > 200)
            .Build(orders)
            .ToList();

        // Assert - 2 shipped orders have total > 200: ORD-002 (250) and ORD-004 (500)
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o =>
        {
            o.Total.Should().BeGreaterThan(200);
            o.Status.Should().Be(OrderStatus.Shipped);
        });
    }

    [Fact]
    public void WithFilter_ComplexExpression_Works()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.Total >= 100 && o.Total <= 200)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o =>
        {
            o.Total.Should().BeGreaterOrEqualTo(100);
            o.Total.Should().BeLessOrEqualTo(200);
        });
    }

    [Fact]
    public void WithFilter_StringContains_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.OrderNumber.Contains("001"))
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].OrderNumber.Should().Contain("001");
    }

    #endregion

    #region OrderBy Tests

    [Fact]
    public void OrderBy_SingleProperty_SortsAscending()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .OrderBy(o => o.Total)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInAscendingOrder(o => o.Total);
    }

    [Fact]
    public void OrderByDescending_SingleProperty_SortsDescending()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .OrderByDescending(o => o.Total)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInDescendingOrder(o => o.Total);
    }

    [Fact]
    public void OrderBy_ThenBy_MultipleSortLevels()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .OrderBy(o => o.Status)
            .ThenByDescending(o => o.Total)
            .Build(orders)
            .ToList();

        // Assert
        // First group by status, then within each status group, descending by total
        var groupedByStatus = result.GroupBy(o => o.Status).ToList();
        foreach (var group in groupedByStatus)
        {
            group.Should().BeInDescendingOrder(o => o.Total);
        }
    }

    #endregion

    #region WithPaging Tests

    [Fact]
    public void WithPaging_FirstPage_ReturnsCorrectItems()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .OrderBy(o => o.CreatedAt)
            .WithPaging(page: 1, pageSize: 2)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void WithPaging_SecondPage_SkipsFirstPageItems()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var page1 = PersistenceQuery.For<Order>()
            .OrderBy(o => o.CreatedAt)
            .WithPaging(page: 1, pageSize: 2)
            .Build(orders)
            .ToList();

        var page2 = PersistenceQuery.For<Order>()
            .OrderBy(o => o.CreatedAt)
            .WithPaging(page: 2, pageSize: 2)
            .Build(orders)
            .ToList();

        // Assert
        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Should().NotIntersectWith(page2);
    }

    [Fact]
    public void WithPaging_LastPage_ReturnRemainingItems()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .OrderBy(o => o.CreatedAt)
            .WithPaging(page: 3, pageSize: 2)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1); // 5 items, page 3 with size 2 = 1 remaining
    }

    [Fact]
    public void WithPaging_PageBeyondData_ReturnsEmpty()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithPaging(page: 10, pageSize: 2)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public void CombinedOperations_FilterSortPage_WorksTogether()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();

        // Act
        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.Total > 100)
            .OrderByDescending(o => o.Total)
            .WithPaging(page: 1, pageSize: 2)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(o => o.Total);
        result.Should().AllSatisfy(o => o.Total.Should().BeGreaterThan(100));
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_AppliesFilterSortAndPaging()
    {
        // Filter keeps {150, 250, 500, 125}; descending sort -> {500, 250, 150, 125};
        // page 1 (size 2) -> {500, 250}.
        var source = CreateTestOrders().AsQueryable();

        var result = PersistenceQuery.For<Order>()
            .WithFilter(o => o.Total >= 100)
            .OrderByDescending(o => o.Total)
            .WithPaging(page: 1, pageSize: 2)
            .Build(source)
            .ToList();

        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(o => o.Total);
        result[0].Total.Should().Be(500m);
        result[1].Total.Should().Be(250m);
    }

    #endregion

    #region Test Data

    private static List<Order> CreateTestOrders()
    {
        var customerId = Guid.NewGuid();
        return
        [
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 150m, Status = OrderStatus.Pending, CreatedAt = DateTime.Now.AddDays(-5), CustomerId = customerId },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 250m, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now.AddDays(-4), CustomerId = customerId },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-003", Total = 75m, Status = OrderStatus.Delivered, CreatedAt = DateTime.Now.AddDays(-3), CustomerId = customerId },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-004", Total = 500m, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now.AddDays(-2), CustomerId = customerId },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-005", Total = 125m, Status = OrderStatus.Cancelled, CreatedAt = DateTime.Now.AddDays(-1), CustomerId = customerId }
        ];
    }

    #endregion

    #region Selective IgnoreQueryFilter is honored

    private sealed class RecordingFilterProvider : Pragmatic.Persistence.Query.Filters.IQueryFilterProvider
    {
        public Pragmatic.Persistence.Query.Filters.FilterContext? LastContext { get; private set; }
        public bool ContextOverloadCalled { get; private set; }

        public IEnumerable<Pragmatic.Persistence.Query.Filters.IQueryFilter<T>> GetFilters<T>() where T : class => [];

        /// <summary>A context that is unmistakably not a fresh one, so the builder can be shown to ask.</summary>
        public Pragmatic.Persistence.Query.Filters.FilterContext AmbientFilterContext()
            => Pragmatic.Persistence.Query.Filters.FilterContext.At(DateTimeOffset.UnixEpoch)
                with { Mode = Pragmatic.Persistence.Query.Filters.FilterMode.Admin };

        public Expression<Func<T, bool>>? GetCombinedFilter<T>(
            Pragmatic.Persistence.Query.Filters.NavigationContext? context = null) where T : class => null;

        public Expression<Func<T, bool>>? GetCombinedFilter<T>(
            Pragmatic.Persistence.Query.Filters.FilterContext filterContext,
            Pragmatic.Persistence.Query.Filters.NavigationContext? navigationContext = null) where T : class
        {
            ContextOverloadCalled = true;
            LastContext = filterContext;
            return null;
        }

        public LambdaExpression? GetCombinedFilter(
            Type entityType,
            Pragmatic.Persistence.Query.Filters.FilterContext filterContext,
            Pragmatic.Persistence.Query.Filters.NavigationContext? navigationContext = null)
        {
            ContextOverloadCalled = true;
            LastContext = filterContext;
            return null;
        }

        public bool HasFilters<T>() where T : class => false;
    }

    private sealed class FakeOwnershipFilter : Pragmatic.Persistence.Query.Filters.IQueryFilter<Order>
    {
        public int Priority => 0;
        public Expression<Func<Order, bool>> GetFilter() => _ => true;
    }

    [Fact]
    public void Build_WithIgnoreQueryFilter_PassesIgnoredTypesToProvider()
    {
        // IgnoreQueryFilter<TFilter>() must reach the provider via the FilterContext.DisabledFilters
        // overload: added to a set that Build() never consumed, the filter would stay active.
        var provider = new RecordingFilterProvider();
        var builder = PersistenceQuery.For<Order>().IgnoreQueryFilter<FakeOwnershipFilter>();

        _ = builder.Build(new List<Order>().AsQueryable(), provider);

        provider.ContextOverloadCalled.Should().BeTrue();
        provider.LastContext!.DisabledFilters.Should().Contain(typeof(FakeOwnershipFilter));
    }

    /// <summary>A read that names no lift still carries the context in scope.</summary>
    /// <remarks>
    ///     ⚠️ A builder naming no lift does not take the overload with no context: the plain overload
    ///     means a blank <c>FilterContext</c>, whose mode is <c>Normal</c> whatever the scope asked
    ///     for, so a mode set with <c>UseMode</c> would never reach a hand-composed read. Which overload is called is an
    ///     implementation choice; what the read is filtered by is the behaviour.
    /// </remarks>
    [Fact]
    public void Build_WithoutIgnoredFilters_StillCarriesTheAmbientContext()
    {
        var provider = new RecordingFilterProvider();
        var builder = PersistenceQuery.For<Order>();

        _ = builder.Build(new List<Order>().AsQueryable(), provider);

        provider.LastContext!.Mode.Should().Be(
            Pragmatic.Persistence.Query.Filters.FilterMode.Admin,
            "the builder asks the provider for the context in scope rather than composing a blank one");
    }

    /// <summary>And a builder lift is added to the ones in scope, not substituted for them.</summary>
    [Fact]
    public void Build_WithIgnoreQueryFilter_KeepsTheAmbientContext()
    {
        var provider = new RecordingFilterProvider();
        var builder = PersistenceQuery.For<Order>().IgnoreQueryFilter<FakeOwnershipFilter>();

        _ = builder.Build(new List<Order>().AsQueryable(), provider);

        provider.LastContext!.Mode.Should().Be(
            Pragmatic.Persistence.Query.Filters.FilterMode.Admin,
            "naming one filter says nothing about the mode");
        provider.LastContext!.DisabledFilters.Should().Contain(typeof(FakeOwnershipFilter));
    }

    #endregion
}
