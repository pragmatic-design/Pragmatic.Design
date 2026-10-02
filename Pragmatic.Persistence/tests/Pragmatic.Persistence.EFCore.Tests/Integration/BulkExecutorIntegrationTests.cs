using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Bulk;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for BulkExecutor using SQLite.
///     Tests bulk insert, bulk upsert, and single upsert with real SQL execution.
/// </summary>
public class BulkExecutorIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BulkTestDbContext _db;

    public BulkExecutorIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BulkTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BulkTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region BulkInsert

    [Fact]
    public async Task BulkInsert_SimpleEntities_InsertsAll()
    {
        var entities = Enumerable.Range(1, 5)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"Product {i}",
                Price = i * 10m
            })
            .ToList();

        var affected = await BulkExecutor.InsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        affected.Should().Be(5);

        var inDb = await _db.Products.ToListAsync();
        inDb.Should().HaveCount(5);
        inDb.Select(p => p.Name).Should().Contain("Product 3");
    }

    [Fact]
    public async Task BulkInsert_EmptyList_ReturnsZero()
    {
        var affected = await BulkExecutor.InsertAsync(
            _db, Array.Empty<BulkProduct>(), BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        affected.Should().Be(0);
    }

    [Fact]
    public async Task BulkInsert_WithBatchSize_InsertAllAcrossBatches()
    {
        var entities = Enumerable.Range(1, 7)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"Batched {i}",
                Price = i * 5m
            })
            .ToList();

        var options = new BulkInsertOptions { BatchSize = 3 };

        var affected = await BulkExecutor.InsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, options, CancellationToken.None);

        affected.Should().Be(7);
        (await _db.Products.CountAsync()).Should().Be(7);
    }

    [Fact]
    public async Task BulkInsert_AuditableEntity_PopulatesCreatedFields()
    {
        var now = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var userId = "user-42";

        var entities = new List<BulkCustomer>
        {
            new()
            {
                PersistenceId = Guid.NewGuid(),
                FullName = "Alice",
                Email = "alice@example.com"
            }
        };

        await BulkExecutor.InsertAsync(
            _db, entities, BulkCustomer.Descriptor, now, userId, null, CancellationToken.None);

        var customer = await _db.Customers.FirstAsync();
        customer.CreatedAt.Should().Be(now);
        customer.CreatedBy.Should().Be(userId);
    }

    [Fact]
    public async Task BulkInsert_SoftDeleteEntity_SetsDefaultSoftDeleteFields()
    {
        var entities = new List<BulkOrder>
        {
            new()
            {
                PersistenceId = Guid.NewGuid(),
                OrderNumber = "ORD-001",
                Total = 100m
            }
        };

        await BulkExecutor.InsertAsync(
            _db, entities, BulkOrder.Descriptor, null, null, null, CancellationToken.None);

        // Bypass query filter to check IsDeleted
        var order = await _db.Orders.IgnoreQueryFilters().FirstAsync();
        order.IsDeleted.Should().BeFalse();
        order.DeletedAt.Should().BeNull();
        order.DeletedBy.Should().BeNull();
    }

    #endregion

    #region BulkUpsert

    [Fact]
    public async Task BulkUpsert_NewEntities_InsertsAll()
    {
        var entities = Enumerable.Range(1, 3)
            .Select(i => new BulkProduct
            {
                PersistenceId = Guid.NewGuid(),
                Name = $"New Product {i}",
                Price = i * 20m
            })
            .ToList();

        var affected = await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        affected.Should().Be(3);
        (await _db.Products.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task BulkUpsert_ExistingEntities_UpdatesThem()
    {
        // Insert first
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _db.Products.AddRange(
            new BulkProduct { PersistenceId = id1, Name = "Original 1", Price = 10m },
            new BulkProduct { PersistenceId = id2, Name = "Original 2", Price = 20m });
        await _db.SaveChangesAsync();

        // Upsert with updated values
        var entities = new List<BulkProduct>
        {
            new() { PersistenceId = id1, Name = "Updated 1", Price = 15m },
            new() { PersistenceId = id2, Name = "Updated 2", Price = 25m }
        };

        await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        // Detach tracked entities to get fresh data
        _db.ChangeTracker.Clear();

        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        products.Should().HaveCount(2);
        products[0].Name.Should().Be("Updated 1");
        products[0].Price.Should().Be(15m);
        products[1].Name.Should().Be("Updated 2");
        products[1].Price.Should().Be(25m);
    }

    [Fact]
    public async Task BulkUpsert_MixOfNewAndExisting_InsertsAndUpdates()
    {
        var existingId = Guid.NewGuid();

        _db.Products.Add(new BulkProduct { PersistenceId = existingId, Name = "Existing", Price = 10m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entities = new List<BulkProduct>
        {
            new() { PersistenceId = existingId, Name = "Updated Existing", Price = 15m },
            new() { PersistenceId = Guid.NewGuid(), Name = "Brand New", Price = 30m }
        };

        await BulkExecutor.UpsertAsync(
            _db, entities, BulkProduct.Descriptor, null, null, null, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        products.Should().HaveCount(2);
        products.Select(p => p.Name).Should().Contain("Updated Existing");
        products.Select(p => p.Name).Should().Contain("Brand New");
    }

    [Fact]
    public async Task BulkUpsert_WithLogicKeyMatch_MatchesOnLogicKey()
    {
        // Insert with a logic key
        _db.LogicKeyProducts.Add(new BulkLogicKeyProduct
        {
            PersistenceId = Guid.NewGuid(),
            Sku = "SKU-001",
            Name = "Original",
            Price = 10m
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Upsert matching on LogicKey (Sku), with a different PersistenceId
        var entities = new List<BulkLogicKeyProduct>
        {
            new()
            {
                PersistenceId = Guid.NewGuid(), // Different PK
                Sku = "SKU-001", // Same logic key
                Name = "Updated via LogicKey",
                Price = 25m
            }
        };

        var options = new UpsertOptions { MatchOn = UpsertMatch.LogicKey };

        await BulkExecutor.UpsertAsync(
            _db, entities, BulkLogicKeyProduct.Descriptor, null, null, options, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var products = await _db.LogicKeyProducts.ToListAsync();
        products.Should().HaveCount(1);
        products[0].Name.Should().Be("Updated via LogicKey");
        products[0].Price.Should().Be(25m);
    }

    #endregion

    #region UpsertSingle

    [Fact]
    public async Task UpsertSingle_NewEntity_InsertsIt()
    {
        var entity = new BulkProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "Single New",
            Price = 42m
        };

        var affected = await BulkExecutor.UpsertSingleAsync(
            _db, entity, BulkProduct.Descriptor, null, null, UpsertMatch.PrimaryKey, CancellationToken.None);

        affected.Should().Be(1);
        (await _db.Products.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertSingle_ExistingEntity_UpdatesIt()
    {
        var id = Guid.NewGuid();

        _db.Products.Add(new BulkProduct { PersistenceId = id, Name = "Before", Price = 10m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var entity = new BulkProduct { PersistenceId = id, Name = "After", Price = 99m };

        await BulkExecutor.UpsertSingleAsync(
            _db, entity, BulkProduct.Descriptor, null, null, UpsertMatch.PrimaryKey, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var product = await _db.Products.FirstAsync();
        product.Name.Should().Be("After");
        product.Price.Should().Be(99m);
    }

    #endregion
}

#region Test entities and descriptors for BulkExecutor

/// <summary>
///     SQLite-backed DbContext for BulkExecutor integration tests.
/// </summary>
internal class BulkTestDbContext(DbContextOptions<BulkTestDbContext> options) : DbContext(options)
{
    public DbSet<BulkProduct> Products { get; set; } = null!;
    public DbSet<BulkOrder> Orders { get; set; } = null!;
    public DbSet<BulkCustomer> Customers { get; set; } = null!;
    public DbSet<BulkLogicKeyProduct> LogicKeyProducts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BulkProduct>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.Price).HasColumnType("REAL");
        });

        modelBuilder.Entity<BulkOrder>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            b.Property(e => e.Total).HasColumnType("REAL");
            b.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<BulkCustomer>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.FullName).IsRequired().HasMaxLength(200);
            b.Property(e => e.Email).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<BulkLogicKeyProduct>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Sku).IsRequired().HasMaxLength(50);
            b.HasIndex(e => e.Sku).IsUnique();
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.Price).HasColumnType("REAL");
        });
    }
}

/// <summary>Simple entity for bulk tests.</summary>
internal class BulkProduct
{
    public Guid PersistenceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public static readonly BulkEntityDescriptor<BulkProduct> Descriptor = new()
    {
        Columns =
        [
            ("PersistenceId", BulkColumnRole.Key),
            ("Name", BulkColumnRole.Regular),
            ("Price", BulkColumnRole.Regular)
        ],
        ReadValue = static (entity, prop) => prop switch
        {
            "PersistenceId" => entity.PersistenceId,
            "Name" => entity.Name,
            "Price" => entity.Price,
            _ => null
        }
    };
}

/// <summary>Soft-delete entity for bulk tests.</summary>
internal class BulkOrder
{
    public Guid PersistenceId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public static readonly BulkEntityDescriptor<BulkOrder> Descriptor = new()
    {
        Columns =
        [
            ("PersistenceId", BulkColumnRole.Key),
            ("OrderNumber", BulkColumnRole.Regular),
            ("Total", BulkColumnRole.Regular),
            ("IsDeleted", BulkColumnRole.SoftDelete),
            ("DeletedAt", BulkColumnRole.SoftDelete),
            ("DeletedBy", BulkColumnRole.SoftDelete)
        ],
        ReadValue = static (entity, prop) => prop switch
        {
            "PersistenceId" => entity.PersistenceId,
            "OrderNumber" => entity.OrderNumber,
            "Total" => entity.Total,
            "IsDeleted" => entity.IsDeleted,
            "DeletedAt" => (object?)entity.DeletedAt,
            "DeletedBy" => (object?)entity.DeletedBy,
            _ => null
        }
    };
}

/// <summary>Auditable entity for bulk tests.</summary>
internal class BulkCustomer
{
    public Guid PersistenceId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public static readonly BulkEntityDescriptor<BulkCustomer> Descriptor = new()
    {
        Columns =
        [
            ("PersistenceId", BulkColumnRole.Key),
            ("FullName", BulkColumnRole.Regular),
            ("Email", BulkColumnRole.Regular),
            ("CreatedAt", BulkColumnRole.InsertOnly),
            ("CreatedBy", BulkColumnRole.InsertOnly),
            ("UpdatedAt", BulkColumnRole.UpdateOnly),
            ("UpdatedBy", BulkColumnRole.UpdateOnly)
        ],
        ReadValue = static (entity, prop) => prop switch
        {
            "PersistenceId" => entity.PersistenceId,
            "FullName" => entity.FullName,
            "Email" => entity.Email,
            "CreatedAt" => entity.CreatedAt,
            "CreatedBy" => (object?)entity.CreatedBy,
            "UpdatedAt" => (object?)entity.UpdatedAt,
            "UpdatedBy" => (object?)entity.UpdatedBy,
            _ => null
        }
    };
}

/// <summary>Entity with logic key for upsert matching tests.</summary>
internal class BulkLogicKeyProduct
{
    public Guid PersistenceId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public static readonly BulkEntityDescriptor<BulkLogicKeyProduct> Descriptor = new()
    {
        Columns =
        [
            ("PersistenceId", BulkColumnRole.Key),
            ("Sku", BulkColumnRole.LogicKey),
            ("Name", BulkColumnRole.Regular),
            ("Price", BulkColumnRole.Regular)
        ],
        ReadValue = static (entity, prop) => prop switch
        {
            "PersistenceId" => entity.PersistenceId,
            "Sku" => entity.Sku,
            "Name" => entity.Name,
            "Price" => entity.Price,
            _ => null
        }
    };
}

#endregion
