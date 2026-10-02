using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Adapters;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     Tests for DevExpress grid adapter.
/// </summary>
public class DevExpressAdapterTests
{
    #region Test Entity

    private class Order
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public decimal Total { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    #endregion

    #region Filter Tests

    [Fact]
    public void FromDevExpress_WithEqualityFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?> { "Status", "=", 1 } // Status == 1 (Shipped)
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.Status.Should().Be(1));
    }

    [Fact]
    public void FromDevExpress_WithStringContainsFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?> { "OrderNumber", "contains", "001" }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].OrderNumber.Should().Contain("001");
    }

    [Fact]
    public void FromDevExpress_WithComparisonFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?> { "Total", ">", 200m }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2); // 250 and 500
        result.Should().AllSatisfy(o => o.Total.Should().BeGreaterThan(200));
    }

    #endregion

    #region Composite Filter Tests

    [Fact]
    public void FromDevExpress_WithAndCompositeFilter_FiltersCorrectly()
    {
        // Arrange: Status == 1 AND Total > 200
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>
            {
                new List<object?> { "Status", "=", 1 },
                "and",
                new List<object?> { "Total", ">", 200m }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert: Only ORD-002 (250, Status=1) and ORD-004 (500, Status=1) match Status=1,
        //         but only those > 200 remain
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o =>
        {
            o.Status.Should().Be(1);
            o.Total.Should().BeGreaterThan(200);
        });
    }

    [Fact]
    public void FromDevExpress_WithOrCompositeFilter_FiltersCorrectly()
    {
        // Arrange: Status == 0 OR Status == 3
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>
            {
                new List<object?> { "Status", "=", 0 },
                "or",
                new List<object?> { "Status", "=", 3 }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert: ORD-001 (Status=0) and ORD-005 (Status=3)
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.Status.Should().BeOneOf(0, 3));
    }

    [Fact]
    public void FromDevExpress_WithMultipleAndFilters_FiltersCorrectly()
    {
        // Arrange: Status == 1 AND Total > 100 AND Total < 300
        // Format: [filter, "and", filter, "and", filter]
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>
            {
                new List<object?> { "Status", "=", 1 },
                "and",
                new List<object?> { "Total", ">", 100m },
                "and",
                new List<object?> { "Total", "<", 300m }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert: Only ORD-002 (250, Status=1) matches all three conditions
        result.Should().HaveCount(1);
        result[0].OrderNumber.Should().Be("ORD-002");
    }

    [Fact]
    public void FromDevExpress_WithNestedCompositeFilter_FiltersCorrectly()
    {
        // Arrange: (Status == 0 AND Total > 100) OR Status == 3
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>
            {
                new List<object?>
                {
                    new List<object?> { "Status", "=", 0 },
                    "and",
                    new List<object?> { "Total", ">", 100m }
                },
                "or",
                new List<object?> { "Status", "=", 3 }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert: ORD-001 (Status=0, Total=150 > 100) and ORD-005 (Status=3)
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.OrderNumber.Should().BeOneOf("ORD-001", "ORD-005"));
    }

    [Fact]
    public void FromDevExpress_WithNegationFilter_FiltersCorrectly()
    {
        // Arrange: NOT (Status == 1) — all orders except Status=1
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>
            {
                "!",
                new List<object?> { "Status", "=", 1 }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert: ORD-001 (Status=0), ORD-003 (Status=2), ORD-005 (Status=3) — 3 orders
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(o => o.Status.Should().NotBe(1));
    }

    [Fact]
    public void FromDevExpress_WithEmptyFilter_ReturnsAll()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?>()
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region Sort Tests

    [Fact]
    public void FromDevExpress_WithSortAscending_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Sort = new List<DevExpressSortDescriptor>
            {
                new() { Selector = "Total", Desc = false }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInAscendingOrder(o => o.Total);
    }

    [Fact]
    public void FromDevExpress_WithSortDescending_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Sort = new List<DevExpressSortDescriptor>
            {
                new() { Selector = "Total", Desc = true }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInDescendingOrder(o => o.Total);
    }

    [Fact]
    public void FromDevExpress_WithMultipleSorts_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Sort = new List<DevExpressSortDescriptor>
            {
                new() { Selector = "Status", Desc = false },
                new() { Selector = "Total", Desc = true }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert - First sorted by status ascending, then by total descending within each status
        var groups = result.GroupBy(o => o.Status).ToList();
        foreach (var group in groups)
        {
            group.Should().BeInDescendingOrder(o => o.Total);
        }
    }

    #endregion

    #region Paging Tests

    [Fact]
    public void FromDevExpress_WithPaging_ReturnsCorrectPage()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Skip = 2,
            Take = 2
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void FromDevExpress_WithSkipBeyondData_ReturnsEmpty()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Skip = 100,
            Take = 10
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void FromDevExpress_WithTakeZeroAndSkipSet_DoesNotThrowDivideByZero()
    {
        // Regression: page = (skip / take) + 1 threw DivideByZeroException when Take == 0
        // (a valid state when only Skip is provided). The guard must treat take == 0 safely.
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Skip = 2,
            Take = 0
        };

        var act = () => PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        act.Should().NotThrow();
    }

    [Fact]
    public void FromDevExpress_WithTakeZero_DoesNotThrowAndClampsPaging()
    {
        // Take == 0 must not throw (no DivideByZero in the adapter's page math).
        // With Take == 0 the adapter passes pageSize 0 to WithPaging, which clamps it
        // to Math.Max(1, 0) == 1 and resets skip to (page-1)*pageSize == 0, so the
        // pipeline returns exactly the first row rather than an empty page.
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Skip = 2,
            Take = 0
        };

        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        result.Should().HaveCount(1);
        result[0].OrderNumber.Should().Be("ORD-001");
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public void FromDevExpress_WithFilterSortAndPaging_WorksTogether()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var options = new DevExpressLoadOptions
        {
            Filter = new List<object?> { "Total", ">=", 100m },
            Sort = new List<DevExpressSortDescriptor>
            {
                new() { Selector = "Total", Desc = true }
            },
            Skip = 0,
            Take = 2
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromDevExpress(options)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(o => o.Total);
        result.Should().AllSatisfy(o => o.Total.Should().BeGreaterOrEqualTo(100));
    }

    #endregion

    #region Test Data

    private static List<Order> CreateTestOrders()
    {
        return
        [
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 150m, Status = 0, CreatedAt = DateTime.Now.AddDays(-5) },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 250m, Status = 1, CreatedAt = DateTime.Now.AddDays(-4) },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-003", Total = 75m, Status = 2, CreatedAt = DateTime.Now.AddDays(-3) },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-004", Total = 500m, Status = 1, CreatedAt = DateTime.Now.AddDays(-2) },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-005", Total = 125m, Status = 3, CreatedAt = DateTime.Now.AddDays(-1) }
        ];
    }

    #endregion
}
