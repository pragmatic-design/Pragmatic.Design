using Pragmatic.Mapping.EFCore.Tests.Data;
using Pragmatic.Mapping.EFCore.Tests.Entities;
using Testcontainers.PostgreSql;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     Base class for PostgreSQL integration tests using TestContainers.
/// </summary>
public abstract class PostgresTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    protected TestDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync().ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        Db = new TestDbContext(options);
        await Db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        await SeedDataAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync().ConfigureAwait(false);
        await _postgres.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Seeds test data. Override to customize.
    /// </summary>
    protected virtual async Task SeedDataAsync()
    {
        var users = new[]
        {
            new User
            {
                Email = "john.doe@example.com",
                FirstName = "John",
                LastName = "Doe",
                CreatedAt = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                Address = new Address
                {
                    Street = "123 Main St",
                    City = "New York",
                    Country = "USA",
                    PostalCode = "10001"
                },
                Orders =
                [
                    new Order
                    {
                        OrderNumber = "ORD-001",
                        Total = 99.99m,
                        Status = OrderStatus.Delivered,
                        OrderDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                        Lines =
                        [
                            new OrderLine { ProductName = "Widget A", Quantity = 2, UnitPrice = 29.99m },
                            new OrderLine { ProductName = "Widget B", Quantity = 1, UnitPrice = 40.01m }
                        ]
                    },
                    new Order
                    {
                        OrderNumber = "ORD-002",
                        Total = 149.50m,
                        Status = OrderStatus.Shipped,
                        OrderDate = new DateTime(2024, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                        Lines =
                        [
                            new OrderLine { ProductName = "Gadget X", Quantity = 1, UnitPrice = 149.50m }
                        ]
                    }
                ]
            },
            new User
            {
                Email = "jane.smith@example.com",
                FirstName = "Jane",
                LastName = "Smith",
                CreatedAt = new DateTime(2024, 2, 20, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                Address = new Address
                {
                    Street = "456 Oak Ave",
                    City = "Los Angeles",
                    Country = "USA",
                    PostalCode = "90001"
                }
            },
            new User
            {
                Email = "inactive@example.com",
                FirstName = "Inactive",
                LastName = "User",
                CreatedAt = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = false
            }
        };

        Db.Users.AddRange(users);
        await Db.SaveChangesAsync().ConfigureAwait(false);
    }
}