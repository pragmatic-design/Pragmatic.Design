using Pragmatic.Mapping.EFCore.Extensions;
using Pragmatic.Mapping.EFCore.Tests.Dtos;
using Pragmatic.Mapping.EFCore.Tests.Entities;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     Integration tests for projection-based mapping with EF Core.
/// </summary>
public class ProjectionTests : PostgresTestBase
{
    // =========================================================================
    // Basic Projection Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_BasicProjection_ReturnsAllUsers()
    {
        // Act
        var users = await Db.Users
            .SelectDto(UserDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.Email == "john.doe@example.com");
        users.Should().Contain(u => u.Email == "jane.smith@example.com");
        users.Should().Contain(u => u.Email == "inactive@example.com");
    }

    [Fact]
    public async Task SelectDto_WithFilter_ReturnsFilteredUsers()
    {
        // Act
        var activeUsers = await Db.Users
            .Where(u => u.IsActive)
            .SelectDto(UserDto.Projection)
            .ToListAsync();

        // Assert
        activeUsers.Should().HaveCount(2);
        activeUsers.Should().OnlyContain(u => u.IsActive);
    }

    // =========================================================================
    // Flattening Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_FlatteningAddressCity_MapsCorrectly()
    {
        // Act
        var user = await Db.Users
            .Where(u => u.Email == "john.doe@example.com")
            .SelectDto(UserDto.Projection)
            .FirstOrDefaultAsync();

        // Assert
        user.Should().NotBeNull();
        user!.AddressCity.Should().Be("New York");
        user.AddressCountry.Should().Be("USA");
    }

    [Fact]
    public async Task SelectDto_FlatteningWithNullAddress_ReturnsNull()
    {
        // Act
        var user = await Db.Users
            .Where(u => u.Email == "inactive@example.com")
            .SelectDto(UserDto.Projection)
            .FirstOrDefaultAsync();

        // Assert
        user.Should().NotBeNull();
        user!.AddressCity.Should().BeNull();
        user.AddressCountry.Should().BeNull();
    }

    // =========================================================================
    // Concatenation Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_ConcatenationFullName_MapsCorrectly()
    {
        // Act
        var users = await Db.Users
            .Where(u => u.IsActive)
            .SelectDto(UserSummaryDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(2);
        users.Should().Contain(u => u.FullName == "John Doe");
        users.Should().Contain(u => u.FullName == "Jane Smith");
    }

    // =========================================================================
    // Extension Methods Tests
    // =========================================================================

    [Fact]
    public async Task ToListDtoAsync_ReturnsProjectedList()
    {
        // Act
        var users = await Db.Users
            .Where(u => u.IsActive)
            .ToListDtoAsync(UserDto.Projection);

        // Assert
        users.Should().HaveCount(2);
    }

    [Fact]
    public async Task FirstOrDefaultDtoAsync_ReturnsFirstMatch()
    {
        // Act
        var user = await Db.Users
            .Where(u => u.Email == "john.doe@example.com")
            .FirstOrDefaultDtoAsync(UserDto.Projection);

        // Assert
        user.Should().NotBeNull();
        user!.Email.Should().Be("john.doe@example.com");
    }

    [Fact]
    public async Task FirstOrDefaultDtoAsync_NoMatch_ReturnsNull()
    {
        // Act
        var user = await Db.Users
            .Where(u => u.Email == "nonexistent@example.com")
            .FirstOrDefaultDtoAsync(UserDto.Projection);

        // Assert
        user.Should().BeNull();
    }

    [Fact]
    public async Task SingleOrDefaultDtoAsync_ReturnsExactMatch()
    {
        // Act
        var user = await Db.Users
            .Where(u => u.Email == "jane.smith@example.com")
            .SingleOrDefaultDtoAsync(UserDto.Projection);

        // Assert
        user.Should().NotBeNull();
        user!.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Smith");
    }

    [Fact]
    public async Task ToArrayDtoAsync_ReturnsProjectedArray()
    {
        // Act
        var users = await Db.Users
            .ToArrayDtoAsync(UserDto.Projection);

        // Assert
        users.Should().BeOfType<UserDto[]>();
        users.Should().HaveCount(3);
    }

    // =========================================================================
    // Order Projection Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_OrderProjection_MapsCorrectly()
    {
        // Act
        var orders = await Db.Orders
            .SelectDto(OrderDto.Projection)
            .ToListAsync();

        // Assert
        orders.Should().HaveCount(2);
        orders.Should().Contain(o => o.OrderNumber == "ORD-001" && o.Total == 99.99m);
        orders.Should().Contain(o => o.OrderNumber == "ORD-002" && o.Total == 149.50m);
    }

    [Fact]
    public async Task SelectDto_OrderByAndProjection_WorksTogether()
    {
        // Act
        var orders = await Db.Orders
            .OrderByDescending(o => o.OrderDate)
            .SelectDto(OrderDto.Projection)
            .ToListAsync();

        // Assert
        orders.Should().HaveCount(2);
        orders[0].OrderNumber.Should().Be("ORD-002"); // July 15
        orders[1].OrderNumber.Should().Be("ORD-001"); // June 1
    }

    // =========================================================================
    // Enum Mapping Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_EnumStatus_MapsCorrectly()
    {
        // Act
        var deliveredOrders = await Db.Orders
            .Where(o => o.Status == OrderStatus.Delivered)
            .SelectDto(OrderDto.Projection)
            .ToListAsync();

        // Assert
        deliveredOrders.Should().HaveCount(1);
        deliveredOrders[0].Status.Should().Be(OrderStatus.Delivered);
    }

    // =========================================================================
    // Pagination Tests
    // =========================================================================

    [Fact]
    public async Task SelectDto_WithSkipAndTake_ReturnsPagedResults()
    {
        // Act
        var users = await Db.Users
            .OrderBy(u => u.Id)
            .Skip(1)
            .Take(1)
            .SelectDto(UserDto.Projection)
            .ToListAsync();

        // Assert
        users.Should().HaveCount(1);
    }

    // =========================================================================
    // A join over an enum reads the same either way
    // =========================================================================

    /// <summary>
    ///     The projected cell carries the enum's <b>name</b>, which is what mapping in memory produces.
    /// </summary>
    /// <remarks>
    ///     Read from the database rather than from the generated text: the defect was that the text
    ///     looked like an ordinary concatenation and the value came back as the number the column holds.
    ///     A generator test comparing strings could not have told the two apart.
    /// </remarks>
    [Fact]
    public async Task SelectDto_JoinOverAnEnum_CarriesTheMemberName()
    {
        var rows = await Db.Orders
            .SelectDto(OrderRowDto.Projection)
            .ToListAsync();

        rows.Should().NotBeEmpty("the seed has orders, or this proves nothing");
        rows.Should().OnlyContain(r => !r.Label.EndsWith("0")
                                       && !r.Label.EndsWith("1")
                                       && !r.Label.EndsWith("2"),
            "a projected enum used to arrive as the number it is stored as");
    }

    /// <summary>
    ///     The control: the projection says exactly what mapping the same row in memory says.
    /// </summary>
    /// <remarks>
    ///     Without it, "the label does not end in a digit" is satisfied by any string at all. This is the
    ///     assertion the defect actually broke — one declaration, two answers — and it compares them.
    /// </remarks>
    [Fact]
    public async Task SelectDto_JoinOverAnEnum_SaysWhatTheInMemoryMappingSays()
    {
        var entity = await Db.Orders.OrderBy(o => o.Id).FirstAsync();

        var projected = await Db.Orders
            .Where(o => o.Id == entity.Id)
            .SelectDto(OrderRowDto.Projection)
            .SingleAsync();

        projected.Label.Should().Be(OrderRowDto.FromEntity(entity).Label,
            "the projection and the in-memory mapping are two readings of one declaration");
    }

    /// <summary>
    ///     And the same holds when the enum is at the end of a dotted path.
    /// </summary>
    /// <remarks>
    ///     The half the first fix left behind: a join part reaching through a navigation kept the plain
    ///     concatenation, so it still projected the number. Closed here.
    /// </remarks>
    [Fact]
    public async Task SelectDto_JoinOverAnEnumThroughANavigation_SaysWhatTheInMemoryMappingSays()
    {
        var entity = await Db.OrderLines.Include(l => l.Order).OrderBy(l => l.Id).FirstAsync();

        var projected = await Db.OrderLines
            .Where(l => l.Id == entity.Id)
            .SelectDto(OrderLineRowDto.Projection)
            .SingleAsync();

        projected.Label.Should().Be(OrderLineRowDto.FromEntity(entity).Label,
            "the path reaches through a navigation, and an enum at the end of one is still an enum");
    }
}
