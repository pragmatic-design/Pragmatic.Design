using Pragmatic.Mapping.EFCore.Extensions;
using Pragmatic.Mapping.EFCore.Tests.Dtos;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     Integration tests for nested DTO projections with EF Core.
///     <para>
///         P0 Test Coverage - Nested Projection Scenarios:
///         <list type="bullet">
///             <item>Single nested DTO (User.Address → UserWithAddressDto.Address)</item>
///             <item>Collection of nested DTOs (Order.Lines → OrderWithLinesDto.Lines)</item>
///             <item>Deep nesting (User.Orders.Lines)</item>
///             <item>Nullable nested DTO handling</item>
///         </list>
///     </para>
///     <para>
///         ✅ IMPLEMENTED: Generator inlines nested DTO property mappings for EF Core SQL translation.
///         Only deep nesting (3+ levels) is still skipped pending recursive inlining support.
///     </para>
/// </summary>
public class NestedProjectionTests : PostgresTestBase
{
    // =========================================================================
    // Single Nested DTO Projection
    // ✅ Implemented: Generator inlines property mappings for EF Core SQL translation
    //    Generates: entity.Address == null ? null : new AddressDto { Id = entity.Address!.Id, ... }
    // =========================================================================

    [Fact]
    public async Task SelectDto_WithNestedAddress_ProjectsCorrectly()
    {
        // Act
        var users = await Db.Users
            .Include(u => u.Address)
            .SelectDto(UserWithAddressDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(3);

        var johnDoe = users.First(u => u.Email == "john.doe@example.com");
        johnDoe.Address.Should().NotBeNull();
        johnDoe.Address!.City.Should().Be("New York");
        johnDoe.Address.Street.Should().Be("123 Main St");
        johnDoe.Address.Country.Should().Be("USA");
        johnDoe.Address.PostalCode.Should().Be("10001");
    }

    [Fact]
    public async Task SelectDto_WithNullNestedAddress_HandlesNullCorrectly()
    {
        // Act
        var inactiveUser = await Db.Users
            .Include(u => u.Address)
            .Where(u => u.Email == "inactive@example.com")
            .SelectDto(UserWithAddressDto.Projection)
            .FirstOrDefaultAsync();

        // Assert
        inactiveUser.Should().NotBeNull();
        inactiveUser!.Address.Should().BeNull();
    }

    [Fact]
    public async Task SelectDto_NestedAddress_WithFilter_FiltersCorrectly()
    {
        // Act
        var usersInNy = await Db.Users
            .Include(u => u.Address)
            .Where(u => u.Address != null && u.Address.City == "New York")
            .SelectDto(UserWithAddressDto.Projection)
            .ToListAsync();

        // Assert
        usersInNy.Should().HaveCount(1);
        usersInNy[0].Email.Should().Be("john.doe@example.com");
    }

    // =========================================================================
    // Collection of Nested DTOs Projection
    // ✅ Implemented: Generator inlines collection element mappings for EF Core SQL translation
    //    Generates: entity.Lines.Select(x => new OrderLineDto { Id = x.Id, ... }).ToList()
    // =========================================================================

    [Fact]
    public async Task SelectDto_WithNestedOrderLines_ProjectsCollection()
    {
        // Act
        var orders = await Db.Orders
            .Include(o => o.Lines)
            .SelectDto(OrderWithLinesDto.Projection)
            .ToListAsync();

        // Assert
        orders.Should().HaveCount(2);

        var order1 = orders.First(o => o.OrderNumber == "ORD-001");
        order1.Lines.Should().HaveCount(2);
        order1.Lines.Should().Contain(l => l.ProductName == "Widget A" && l.Quantity == 2);
        order1.Lines.Should().Contain(l => l.ProductName == "Widget B" && l.Quantity == 1);

        var order2 = orders.First(o => o.OrderNumber == "ORD-002");
        order2.Lines.Should().HaveCount(1);
        order2.Lines[0].ProductName.Should().Be("Gadget X");
    }

    [Fact]
    public async Task SelectDto_WithNestedOrders_ProjectsUserOrders()
    {
        // Act
        var users = await Db.Users
            .Include(u => u.Orders)
            .Where(u => u.Email == "john.doe@example.com")
            .SelectDto(UserWithOrdersDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(1);
        users[0].Orders.Should().HaveCount(2);
        users[0].Orders.Should().Contain(o => o.OrderNumber == "ORD-001");
        users[0].Orders.Should().Contain(o => o.OrderNumber == "ORD-002");
    }

    [Fact]
    public async Task SelectDto_WithEmptyNestedCollection_ReturnsEmptyList()
    {
        // Act - Jane Smith has no orders
        var jane = await Db.Users
            .Include(u => u.Orders)
            .Where(u => u.Email == "jane.smith@example.com")
            .SelectDto(UserWithOrdersDto.Projection)
            .FirstOrDefaultAsync();

        // Assert
        jane.Should().NotBeNull();
        jane!.Orders.Should().BeEmpty();
    }

    // =========================================================================
    // Deep Nesting (3 levels)
    // ✅ Implemented: Generator recursively inlines nested DTO projections.
    // =========================================================================

    [Fact]
    public async Task SelectDto_DeepNesting_ProjectsAllLevels()
    {
        // Act - User -> Orders -> Lines (3 levels)
        var users = await Db.Users
            .Include(u => u.Address)
            .Include(u => u.Orders)
            .ThenInclude(o => o.Lines)
            .Where(u => u.Email == "john.doe@example.com")
            .SelectDto(UserFullDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(1);

        var user = users[0];
        user.Address.Should().NotBeNull();
        user.Address!.City.Should().Be("New York");

        user.Orders.Should().HaveCount(2);

        var order1 = user.Orders.First(o => o.OrderNumber == "ORD-001");
        order1.Lines.Should().HaveCount(2);
        order1.Lines.Should().Contain(l => l.ProductName == "Widget A");
    }

    // =========================================================================
    // Query Composition with Nested DTOs
    // ✅ Implemented: Works with OrderBy, Skip, Take, etc.
    // =========================================================================

    [Fact]
    public async Task SelectDto_NestedWithOrderBy_OrdersCorrectly()
    {
        // Act
        var orders = await Db.Orders
            .Include(o => o.Lines)
            .OrderByDescending(o => o.Total)
            .SelectDto(OrderWithLinesDto.Projection)
            .ToListAsync();

        // Assert
        orders.Should().HaveCount(2);
        orders[0].Total.Should().Be(149.50m); // Higher total first
        orders[1].Total.Should().Be(99.99m);
    }

    [Fact]
    public async Task SelectDto_NestedWithPagination_PaginatesCorrectly()
    {
        // Act
        var orders = await Db.Orders
            .Include(o => o.Lines)
            .OrderBy(o => o.OrderNumber)
            .Skip(1)
            .Take(1)
            .SelectDto(OrderWithLinesDto.Projection)
            .ToListAsync();

        // Assert
        orders.Should().HaveCount(1);
        orders[0].OrderNumber.Should().Be("ORD-002");
    }

    // =========================================================================
    // Extension Methods with Nested DTOs
    // ✅ Implemented: ToListDtoAsync, FirstOrDefaultDtoAsync work with nested projections
    // =========================================================================

    [Fact]
    public async Task ToListDtoAsync_WithNestedDto_ReturnsProjectedList()
    {
        // Act
        var users = await Db.Users
            .Include(u => u.Address)
            .Where(u => u.IsActive)
            .ToListDtoAsync(UserWithAddressDto.Projection);

        // Assert
        users.Should().HaveCount(2);
        users.Should().OnlyContain(u => u.IsActive);
    }

    [Fact]
    public async Task FirstOrDefaultDtoAsync_WithNestedDto_ReturnsFirst()
    {
        // Act
        var user = await Db.Users
            .Include(u => u.Address)
            .Where(u => u.Address != null && u.Address.City == "Los Angeles")
            .FirstOrDefaultDtoAsync(UserWithAddressDto.Projection);

        // Assert
        user.Should().NotBeNull();
        user!.Email.Should().Be("jane.smith@example.com");
        user.Address.Should().NotBeNull();
        user.Address!.City.Should().Be("Los Angeles");
    }
}