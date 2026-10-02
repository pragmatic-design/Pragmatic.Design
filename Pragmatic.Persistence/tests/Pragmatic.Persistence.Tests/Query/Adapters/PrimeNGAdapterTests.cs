using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Adapters;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     Tests for PrimeNG grid adapter.
/// </summary>
public class PrimeNGAdapterTests
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
    public void FromPrimeNG_WithEqualsFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["Status"] = new() { Value = 1, MatchMode = "equals" }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.Status.Should().Be(1));
    }

    [Fact]
    public void FromPrimeNG_WithContainsFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["OrderNumber"] = new() { Value = "001", MatchMode = "contains" }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].OrderNumber.Should().Contain("001");
    }

    [Fact]
    public void FromPrimeNG_WithStartsWithFilter_FiltersCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["OrderNumber"] = new() { Value = "ORD-00", MatchMode = "startsWith" }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(5); // All start with ORD-00
        result.Should().AllSatisfy(o => o.OrderNumber.Should().StartWith("ORD-00"));
    }

    [Fact]
    public void FromPrimeNG_WithMultipleFilters_CombinesWithAnd()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["Status"] = new() { Value = 1, MatchMode = "equals" },
                ["OrderNumber"] = new() { Value = "002", MatchMode = "contains" }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Status.Should().Be(1);
        result[0].OrderNumber.Should().Contain("002");
    }

    #endregion

    #region Sort Tests

    [Fact]
    public void FromPrimeNG_WithSortAscending_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            SortField = "Total",
            SortOrder = 1 // Ascending
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInAscendingOrder(o => o.Total);
    }

    [Fact]
    public void FromPrimeNG_WithSortDescending_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            SortField = "Total",
            SortOrder = -1 // Descending
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeInDescendingOrder(o => o.Total);
    }

    [Fact]
    public void FromPrimeNG_WithMultiSort_SortsCorrectly()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            MultiSortMeta = new List<PrimeNGSortMeta>
            {
                new() { Field = "Status", Order = 1 },
                new() { Field = "Total", Order = -1 }
            }
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
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
    public void FromPrimeNG_WithPaging_ReturnsCorrectPage()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            First = 2, // Skip first 2
            Rows = 2   // Take 2
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void FromPrimeNG_WithFirstBeyondData_ReturnsEmpty()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            First = 100,
            Rows = 10
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GlobalFilter Tests

    [Fact]
    public void FromPrimeNG_WithGlobalFilter_FiltersAcrossFields()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            GlobalFilter = "001"
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert - Should match OrderNumber containing "001"
        result.Should().NotBeEmpty();
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public void FromPrimeNG_WithFilterSortAndPaging_WorksTogether()
    {
        // Arrange
        var orders = CreateTestOrders().AsQueryable();
        var lazyLoadEvent = new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["Status"] = new() { Value = 1, MatchMode = "equals" }
            },
            SortField = "Total",
            SortOrder = -1,
            First = 0,
            Rows = 1
        };

        // Act
        var result = PersistenceQuery.For<Order>()
            .FromPrimeNG(lazyLoadEvent)
            .Build(orders)
            .ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Status.Should().Be(1);
        result[0].Total.Should().Be(500m); // Highest total with status 1
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
