using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for soft-delete behavior with EF Core query filters.
///     Verifies that soft-deleted entities are excluded by default and can be force-included.
/// </summary>
public class SoftDeleteIntegrationTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContextFactory.Create();

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Query_WithSoftDeleteFilter_ExcludesDeletedEntities()
    {
        // Arrange
        var activeOrder = new TestOrder
        {
            PersistenceId = Guid7.New(),
            OrderNumber = "ORD-001",
            Total = 100.00m,
            IsDeleted = false
        };

        var deletedOrder = new TestOrder
        {
            PersistenceId = Guid7.New(),
            OrderNumber = "ORD-002",
            Total = 200.00m,
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _db.Orders.AddRange(activeOrder, deletedOrder);
        await _db.SaveChangesAsync();

        // Act
        var orders = await _db.Orders.ToListAsync();

        // Assert - query filter excludes soft-deleted entities
        orders.Should().HaveCount(1);
        orders[0].OrderNumber.Should().Be("ORD-001");
    }

    [Fact]
    public async Task Query_WithIgnoreQueryFilters_IncludesDeletedEntities()
    {
        // Arrange
        _db.Orders.AddRange(
            new TestOrder
            {
                PersistenceId = Guid7.New(),
                OrderNumber = "ORD-010",
                Total = 50.00m,
                IsDeleted = false
            },
            new TestOrder
            {
                PersistenceId = Guid7.New(),
                OrderNumber = "ORD-011",
                Total = 75.00m,
                IsDeleted = true,
                DeletedAt = DateTimeOffset.UtcNow
            });

        await _db.SaveChangesAsync();

        // Act - force include deleted via IgnoreQueryFilters
        var allOrders = await _db.Orders.IgnoreQueryFilters().ToListAsync();

        // Assert
        allOrders.Should().HaveCount(2);
    }

    [Fact]
    public async Task SoftDelete_SetsIsDeletedAndDeletedAt()
    {
        // Arrange
        var id = Guid7.New();
        var order = new TestOrder
        {
            PersistenceId = id,
            OrderNumber = "ORD-020",
            Total = 300.00m,
            IsDeleted = false
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // Act - simulate soft delete (as the generated repository would do)
        order.IsDeleted = true;
        order.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        // Assert - not visible in normal queries
        var normalQuery = await _db.Orders.FirstOrDefaultAsync(o => o.PersistenceId == id);
        normalQuery.Should().BeNull();

        // Assert - visible with IgnoreQueryFilters
        var unfiltered = await _db.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PersistenceId == id);

        unfiltered.Should().NotBeNull();
        unfiltered!.IsDeleted.Should().BeTrue();
        unfiltered.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Count_WithSoftDeleteFilter_ExcludesDeletedEntities()
    {
        // Arrange
        _db.Orders.AddRange(
            new TestOrder { PersistenceId = Guid7.New(), OrderNumber = "ORD-030", Total = 10m, IsDeleted = false },
            new TestOrder { PersistenceId = Guid7.New(), OrderNumber = "ORD-031", Total = 20m, IsDeleted = false },
            new TestOrder { PersistenceId = Guid7.New(), OrderNumber = "ORD-032", Total = 30m, IsDeleted = true, DeletedAt = DateTimeOffset.UtcNow });

        await _db.SaveChangesAsync();

        // Act
        var activeCount = await _db.Orders.CountAsync();
        var totalCount = await _db.Orders.IgnoreQueryFilters().CountAsync();

        // Assert
        activeCount.Should().Be(2);
        totalCount.Should().Be(3);
    }

    [Fact]
    public async Task FindAsync_SoftDeletedEntity_StillReturnsByPrimaryKey()
    {
        // Arrange - FindAsync bypasses query filters by design in EF Core
        var id = Guid7.New();
        _db.Orders.Add(new TestOrder
        {
            PersistenceId = id,
            OrderNumber = "ORD-040",
            Total = 99.00m,
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act - FindAsync uses the identity map / primary key lookup
        var result = await _db.Orders.FindAsync(id);

        // Assert - FindAsync returns entities regardless of query filter
        result.Should().NotBeNull();
        result!.IsDeleted.Should().BeTrue();
    }
}
