using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Bulk;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for BulkExecutor against PostgreSQL via Testcontainers.
///     Tests real INSERT ON CONFLICT syntax, EXCLUDED references, and audit field handling.
/// </summary>
[Trait("Category", "Testcontainers")]
public class BulkExecutorPostgresTests(PostgresContainerFixture fixture) : IAsyncLifetime, IClassFixture<PostgresContainerFixture>
{
    /// <summary>
    ///     A database of this test's own, inside the class's container.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The container is shared by the class and the database is not, deliberately: these tests
    ///     count rows, and rows another test inserted would be in the count. Creating a database costs
    ///     milliseconds; starting a container cost seconds, once per test.
    /// </remarks>
    private readonly string _database = "bulk_" + Guid.NewGuid().ToString("N");

    private BulkTestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var builder = new global::Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = _database
        };

        var options = new DbContextOptionsBuilder<BulkTestDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        _db = new BulkTestDbContext(options);

        // Creates the database as well as the schema, which is why no CREATE DATABASE is needed here.
        await _db.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await _db.DisposeAsync().ConfigureAwait(false);
    }

    #region BulkInsert

    [Fact]
    public async Task BulkInsert_SimpleEntities_InsertsAll()
    {
        var entities = Enumerable.Range(1, 10)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"PG Product {i}",
                Price = i * 10m
            })
            .ToList();

        var affected = await BulkExecutor.InsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        affected.Should().Be(10);
        (await _db.Products.CountAsync()).Should().Be(10);
    }

    [Fact]
    public async Task BulkInsert_WithBatching_InsertsAcrossMultipleBatches()
    {
        var entities = Enumerable.Range(1, 25)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"PG Batch {i}",
                Price = i * 2m
            })
            .ToList();

        var options = new BulkInsertOptions { BatchSize = 7 };

        var affected = await BulkExecutor.InsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, options, CancellationToken.None);

        affected.Should().Be(25);
        (await _db.Products.CountAsync()).Should().Be(25);
    }

    [Fact]
    public async Task BulkInsert_AuditableEntity_SetsCreatedAtAndCreatedBy()
    {
        var now = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var userId = "pg-admin";

        var entities = new List<BulkCustomer>
        {
            new() { PersistenceId = Guid.NewGuid(), FullName = "PG Alice", Email = "alice@pg.com" },
            new() { PersistenceId = Guid.NewGuid(), FullName = "PG Bob", Email = "bob@pg.com" }
        };

        await BulkExecutor.InsertAsync(
            _db, entities, BulkCustomer.Descriptor, now, userId, null, CancellationToken.None);

        var customers = await _db.Customers.OrderBy(c => c.FullName).ToListAsync();
        customers.Should().HaveCount(2);

        foreach (var c in customers)
        {
            c.CreatedAt.Should().Be(now);
            c.CreatedBy.Should().Be(userId);
            c.UpdatedAt.Should().BeNull();
            c.UpdatedBy.Should().BeNull();
        }
    }

    [Fact]
    public async Task BulkInsert_SoftDeleteEntity_DefaultsToNotDeleted()
    {
        var entities = new List<BulkOrder>
        {
            new() { PersistenceId = Guid.NewGuid(), OrderNumber = "PG-001", Total = 200m }
        };

        await BulkExecutor.InsertAsync(
            _db, entities, BulkOrder.Descriptor, null, null, null, CancellationToken.None);

        var order = await _db.Orders.IgnoreQueryFilters().FirstAsync();
        order.IsDeleted.Should().BeFalse();
        order.DeletedAt.Should().BeNull();
    }

    #endregion

    #region BulkUpsert (ON CONFLICT)

    [Fact]
    public async Task BulkUpsert_NewEntities_InsertsViaOnConflict()
    {
        var entities = Enumerable.Range(1, 5)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"PG Upsert New {i}",
                Price = i * 12m
            })
            .ToList();

        var affected = await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        affected.Should().Be(5);
        (await _db.Products.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task BulkUpsert_ExistingEntities_UpdatesViaOnConflict()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _db.Products.AddRange(
            new BulkProduct { PersistenceId = id1, Name = "PG Before 1", Price = 10m },
            new BulkProduct { PersistenceId = id2, Name = "PG Before 2", Price = 20m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entities = new List<BulkProduct>
        {
            new() { PersistenceId = id1, Name = "PG After 1", Price = 15m },
            new() { PersistenceId = id2, Name = "PG After 2", Price = 25m }
        };

        await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        products.Should().HaveCount(2);
        products[0].Name.Should().Be("PG After 1");
        products[1].Name.Should().Be("PG After 2");
    }

    [Fact]
    public async Task BulkUpsert_MixInsertAndUpdate()
    {
        var existingId = Guid.NewGuid();
        _db.Products.Add(new BulkProduct { PersistenceId = existingId, Name = "PG Existing", Price = 10m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entities = new List<BulkProduct>
        {
            new() { PersistenceId = existingId, Name = "PG Updated", Price = 50m },
            new() { PersistenceId = Guid.NewGuid(), Name = "PG Brand New", Price = 30m }
        };

        await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        products.Should().HaveCount(2);
        products.Select(p => p.Name).Should().Contain("PG Updated");
        products.Select(p => p.Name).Should().Contain("PG Brand New");
    }

    [Fact]
    public async Task BulkUpsert_LogicKeyMatch_MatchesOnSku()
    {
        _db.LogicKeyProducts.Add(new BulkLogicKeyProduct
        {
            PersistenceId = Guid.NewGuid(),
            Sku = "PG-SKU-001",
            Name = "PG Original",
            Price = 10m
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entities = new List<BulkLogicKeyProduct>
        {
            new()
            {
                PersistenceId = Guid.NewGuid(),
                Sku = "PG-SKU-001",
                Name = "PG Updated via LogicKey",
                Price = 88m
            }
        };

        var options = new UpsertOptions { MatchOn = UpsertMatch.LogicKey };
        await BulkExecutor.UpsertAsync(
            _db, entities, BulkLogicKeyProduct.Descriptor, null, null, options, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var products = await _db.LogicKeyProducts.ToListAsync();
        products.Should().HaveCount(1);
        products[0].Name.Should().Be("PG Updated via LogicKey");
        products[0].Price.Should().Be(88m);
    }

    [Fact]
    public async Task BulkUpsert_AuditableEntity_SetsUpdatedAtOnUpdate()
    {
        var insertTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var updateTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();

        await BulkExecutor.InsertAsync(
            _db,
            new List<BulkCustomer>
            {
                new() { PersistenceId = id, FullName = "PG Alice", Email = "alice@pg.com" }
            },
            BulkCustomer.Descriptor, insertTime, "creator", null, CancellationToken.None);
        _db.ChangeTracker.Clear();

        await BulkExecutor.UpsertAsync(
            _db,
            new List<BulkCustomer>
            {
                new() { PersistenceId = id, FullName = "PG Alice Updated", Email = "alice@new.pg.com" }
            },
            BulkCustomer.Descriptor, updateTime, "updater", null, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var customer = await _db.Customers.FirstAsync();

        customer.CreatedAt.Should().Be(insertTime);
        customer.CreatedBy.Should().Be("creator");
        customer.UpdatedAt.Should().Be(updateTime);
        customer.UpdatedBy.Should().Be("updater");
        customer.FullName.Should().Be("PG Alice Updated");
    }

    #endregion

    #region UpsertSingle

    [Fact]
    public async Task UpsertSingle_NewEntity_Inserts()
    {
        var entity = new BulkProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "PG Single Insert",
            Price = 42m
        };

        var affected = await BulkExecutor.UpsertSingleAsync(
            _db, entity, BulkProduct.Descriptor, null, null, UpsertMatch.PrimaryKey, CancellationToken.None);

        affected.Should().Be(1);
        (await _db.Products.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertSingle_ExistingEntity_Updates()
    {
        var id = Guid.NewGuid();
        _db.Products.Add(new BulkProduct { PersistenceId = id, Name = "PG Before", Price = 10m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entity = new BulkProduct { PersistenceId = id, Name = "PG After", Price = 77m };

        await BulkExecutor.UpsertSingleAsync(
            _db, entity, BulkProduct.Descriptor, null, null, UpsertMatch.PrimaryKey, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var product = await _db.Products.FirstAsync();
        product.Name.Should().Be("PG After");
        product.Price.Should().Be(77m);
    }

    #endregion
}
