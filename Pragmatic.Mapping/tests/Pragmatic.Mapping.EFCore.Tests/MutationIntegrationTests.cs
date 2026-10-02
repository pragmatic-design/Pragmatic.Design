using Pragmatic.Mapping.EFCore.Tests.Entities;
using Pragmatic.Mapping.Mutation;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     Integration tests for <see cref="MutationHelpers" /> against a real EF Core change-tracked
///     collection. Exercises
///     add / update / remove against PostgreSQL with a cascade one-to-many (User → Orders).
/// </summary>
public class MutationIntegrationTests : PostgresTestBase
{
    private record OrderInput(int Id, string OrderNumber, decimal Total);

    [Fact]
    public async Task MapOneToMany_Sync_AddsUpdatesRemoves_PersistsToDatabase()
    {
        var user = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var keepId = user.Orders.Single(o => o.OrderNumber == "ORD-001").Id;

        var desired = new List<OrderInput>
        {
            new(keepId, "ORD-001", 199.99m), // existing → update
            new(0, "ORD-003", 75.00m)        // new → add   (ORD-002 omitted → remove)
        };

        MutationHelpers.MapOneToMany(
            desired,
            user.Orders,
            d => d.Id,
            e => e.Id,
            factory: d => new Order
            {
                OrderNumber = d.OrderNumber,
                Total = d.Total,
                Status = OrderStatus.Pending,
                OrderDate = new DateTime(2024, 8, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            updater: (d, e) => e.Total = d.Total,
            strategy: CollectionStrategy.Sync);

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        reloaded.Orders.Select(o => o.OrderNumber).Should().BeEquivalentTo("ORD-001", "ORD-003");
        reloaded.Orders.Single(o => o.OrderNumber == "ORD-001").Total.Should().Be(199.99m);
        reloaded.Orders.Should().NotContain(o => o.OrderNumber == "ORD-002"); // removed (orphan cascade)
    }

    [Fact]
    public async Task MapOneToMany_AddOnly_KeepsMissingItems()
    {
        var user = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var desired = new List<OrderInput> { new(0, "ORD-NEW", 10m) }; // only a new one

        MutationHelpers.MapOneToMany(
            desired,
            user.Orders,
            d => d.Id,
            e => e.Id,
            factory: d => new Order
            {
                OrderNumber = d.OrderNumber,
                Total = d.Total,
                Status = OrderStatus.Pending,
                OrderDate = new DateTime(2024, 8, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            strategy: CollectionStrategy.AddOnly);

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        // AddOnly never removes: the two seeded orders survive plus the new one.
        reloaded.Orders.Select(o => o.OrderNumber)
            .Should().BeEquivalentTo("ORD-001", "ORD-002", "ORD-NEW");
    }
}
