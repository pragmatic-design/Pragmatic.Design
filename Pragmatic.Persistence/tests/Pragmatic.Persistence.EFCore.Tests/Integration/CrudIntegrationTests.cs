using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for basic CRUD operations using EF Core InMemory provider.
///     Validates that entities with Guid7 IDs can be created, read, updated, and deleted.
/// </summary>
public class CrudIntegrationTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContextFactory.Create();

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Add_NewEntity_PersistsToDatabase()
    {
        // Arrange
        var id = Guid7.New();
        var product = new TestProduct
        {
            PersistenceId = id,
            Name = "Widget",
            Price = 19.99m
        };

        // Act
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Products.FindAsync(id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Widget");
        loaded.Price.Should().Be(19.99m);
    }

    [Fact]
    public async Task GetById_ExistingEntity_ReturnsEntity()
    {
        // Arrange
        var id = Guid7.New();
        _db.Products.Add(new TestProduct { PersistenceId = id, Name = "Gadget", Price = 42.00m });
        await _db.SaveChangesAsync();

        // Act
        var result = await _db.Products.FirstOrDefaultAsync(p => p.PersistenceId == id);

        // Assert
        result.Should().NotBeNull();
        result!.PersistenceId.Should().Be(id);
        result.Name.Should().Be("Gadget");
    }

    [Fact]
    public async Task GetById_NonExistingEntity_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid7.New();

        // Act
        var result = await _db.Products.FirstOrDefaultAsync(p => p.PersistenceId == nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Update_ExistingEntity_PersistsChanges()
    {
        // Arrange
        var id = Guid7.New();
        _db.Products.Add(new TestProduct { PersistenceId = id, Name = "OldName", Price = 10.00m });
        await _db.SaveChangesAsync();

        // Act
        var product = await _db.Products.FindAsync(id);
        product!.Name = "NewName";
        product.Price = 25.50m;
        await _db.SaveChangesAsync();

        // Assert
        var updated = await _db.Products.FindAsync(id);
        updated!.Name.Should().Be("NewName");
        updated.Price.Should().Be(25.50m);
    }

    [Fact]
    public async Task Delete_ExistingEntity_RemovesFromDatabase()
    {
        // Arrange
        var id = Guid7.New();
        _db.Products.Add(new TestProduct { PersistenceId = id, Name = "Temp", Price = 5.00m });
        await _db.SaveChangesAsync();

        // Act
        var product = await _db.Products.FindAsync(id);
        _db.Products.Remove(product!);
        await _db.SaveChangesAsync();

        // Assert
        var deleted = await _db.Products.FindAsync(id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task AddRange_MultipleEntities_PersistsAll()
    {
        // Arrange
        var products = Enumerable.Range(1, 5).Select(i => new TestProduct
        {
            PersistenceId = Guid7.New(),
            Name = $"Product_{i}",
            Price = i * 10.0m
        }).ToList();

        // Act
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();

        // Assert
        var count = await _db.Products.CountAsync();
        count.Should().Be(5);
    }

    [Fact]
    public async Task Guid7Id_IsVersion7_AfterPersistence()
    {
        // Arrange - use Guid7.New() which produces UUIDv7
        var id = Guid7.New();
        _db.Products.Add(new TestProduct { PersistenceId = id, Name = "Versioned", Price = 1.00m });
        await _db.SaveChangesAsync();

        // Act
        var loaded = await _db.Products.FindAsync(id);

        // Assert
        loaded.Should().NotBeNull();
        Guid7.IsVersion7(loaded!.PersistenceId).Should().BeTrue();
    }
}
