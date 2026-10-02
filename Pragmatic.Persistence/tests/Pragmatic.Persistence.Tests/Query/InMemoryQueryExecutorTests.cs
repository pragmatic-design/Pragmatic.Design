using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Executors;
using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.Tests.QueryTests;

/// <summary>
///     Tests for InMemoryQueryExecutor.
/// </summary>
public class InMemoryQueryExecutorTests
{
    #region Test Entities and Queries

    private class Order
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public decimal Total { get; set; }
        public Guid CustomerId { get; set; }
    }

    private class OrderDto
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
    }

    /// <summary>
    ///     Simple query implementation for testing.
    /// </summary>
    private class GetOrdersByCustomer : IPagedQuery<Order>
    {
        public required Guid CustomerId { get; init; }
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 10;

        public IQueryable<Order> Apply(IQueryable<Order> query)
        {
            return query.Where(o => o.CustomerId == CustomerId);
        }
    }

    /// <summary>
    ///     Query that returns all items.
    /// </summary>
    private class GetAllOrders : IQuery<Order>
    {
        public IQueryable<Order> Apply(IQueryable<Order> query) => query;
    }

    /// <summary>
    ///     Projecting paged query: maps Order -> OrderDto via an expression.
    /// </summary>
    private class GetOrderDtos : IPagedQuery<Order, OrderDto>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 10;

        public IQueryable<Order> Apply(IQueryable<Order> query) => query;

        public Expression<Func<Order, OrderDto>>? Projection =>
            o => new OrderDto { Id = o.Id, OrderNumber = o.OrderNumber };
    }

    /// <summary>
    ///     Projecting paged query with a null projection (relies on cast/OfType fallback).
    /// </summary>
    private class GetOrdersNoProjection : IPagedQuery<Order, OrderDto>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 10;

        public IQueryable<Order> Apply(IQueryable<Order> query) => query;

        public Expression<Func<Order, OrderDto>>? Projection => null;
    }

    /// <summary>
    ///     Unpaged projecting query: maps Order -> OrderDto. Exercises the ExecuteAllAsync projected overload
    ///     used by published read contracts (#3).
    /// </summary>
    private class GetOrderDtosUnpaged : IQuery<Order, OrderDto>
    {
        public IQueryable<Order> Apply(IQueryable<Order> query) => query.Where(o => o.Total > 0);

        public Expression<Func<Order, OrderDto>>? Projection =>
            o => new OrderDto { Id = o.Id, OrderNumber = o.OrderNumber };
    }

    #endregion

    #region Singleton Instance Tests

    [Fact]
    public void Instance_ReturnsSameInstance()
    {
        // Act
        var instance1 = InMemoryQueryExecutor.Instance;
        var instance2 = InMemoryQueryExecutor.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithMatchingItems_ReturnsFilteredResults()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orders = new List<Order>
        {
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-001", CustomerId = customerId },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-002", CustomerId = customerId },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-003", CustomerId = Guid.NewGuid() } // Different customer
        }.AsQueryable();

        var query = new GetOrdersByCustomer { CustomerId = customerId };
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Items.Should().AllSatisfy(o => o.CustomerId.Should().Be(customerId));
    }

    [Fact]
    public async Task ExecuteAsync_WithNoMatchingItems_ReturnsEmptyResult()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var query = new GetOrdersByCustomer { CustomerId = Guid.NewGuid() }; // Non-existent customer
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithPaging_ReturnsPaginatedResults()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orders = Enumerable.Range(1, 15)
            .Select(i => new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = $"ORD-{i:000}",
                CustomerId = customerId
            })
            .AsQueryable();

        var query = new GetOrdersByCustomer
        {
            CustomerId = customerId,
            Page = 2,
            PageSize = 5
        };
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(15);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    #endregion

    #region Projection Overload Tests

    [Fact]
    public async Task ExecuteAsync_WithProjection_MapsToResultType()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var query = new GetOrderDtos();
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllBeOfType<OrderDto>();
        result.Items.Select(d => d.OrderNumber).Should().Contain("ORD-001");
    }

    [Fact]
    public async Task ExecuteAsync_WithProjectionAndPaging_CountsBeforePaging()
    {
        // Arrange: 15 orders, page 2 of size 5 -> TotalCount=15, 5 items returned.
        var orders = Enumerable.Range(1, 15)
            .Select(i => new Order { Id = Guid.NewGuid(), OrderNumber = $"ORD-{i:000}" })
            .AsQueryable();
        var query = new GetOrderDtos { Page = 2, PageSize = 5 };
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalCount.Should().Be(15);
        result.Items.Should().HaveCount(5);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_NullProjectionWithIncompatibleType_ReturnsEmptyNotThrows()
    {
        // Regression: a null projection used the OfType<TResult> fallback. Order is NOT an
        // OrderDto, so the result must be a successful empty page rather than a thrown cast.
        var orders = CreateTestOrders().AsQueryable();
        var query = new GetOrdersNoProjection();
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAsync(query, orders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(2); // count reflects matched entities before projection
    }

    #endregion

    #region ExecuteAllAsync Tests

    [Fact]
    public async Task ExecuteAllAsync_ReturnsAllMatchingItems()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orders = new List<Order>
        {
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-001", CustomerId = customerId },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-002", CustomerId = customerId },
        }.AsQueryable();

        var query = new GetAllOrders();
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAllAsync(query, orders);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAllAsync_Projected_AppliesFilterAndProjection()
    {
        // Arrange
        var orders = new List<Order>
        {
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 10m },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 20m },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-FREE", Total = 0m },   // filtered out (Total > 0)
        }.AsQueryable();

        var query = new GetOrderDtosUnpaged();
        var executor = InMemoryQueryExecutor.Instance;

        // Act
        var result = await executor.ExecuteAllAsync(query, orders);

        // Assert — projected to OrderDto, the zero-total order excluded.
        result.Should().HaveCount(2);
        result.Should().AllBeOfType<OrderDto>();
        result.Select(d => d.OrderNumber).Should().BeEquivalentTo(["ORD-001", "ORD-002"]);
    }

    #endregion

    #region Test Data

    private static List<Order> CreateTestOrders()
    {
        var customerId = Guid.NewGuid();
        return
        [
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 100m, CustomerId = customerId },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 200m, CustomerId = customerId },
        ];
    }

    #endregion
}
