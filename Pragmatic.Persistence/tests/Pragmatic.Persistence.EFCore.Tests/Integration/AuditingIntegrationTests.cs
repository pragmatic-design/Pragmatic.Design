using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Integration tests for auditing behavior (CreatedAt/UpdatedAt auto-population).
///     Validates that the SaveChanges override populates audit fields correctly.
/// </summary>
public class AuditingIntegrationTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContextFactory.Create();

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SaveChanges_NewEntity_SetsCreatedAtAndUpdatedAt()
    {
        // Arrange
        var beforeSave = DateTimeOffset.UtcNow;
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "John Doe",
            Email = "john@example.com"
        };

        // Act
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedAt.Should().BeOnOrAfter(beforeSave);
        loaded.UpdatedAt.Should().NotBeNull();
        loaded.UpdatedAt.Should().BeOnOrAfter(beforeSave);
    }

    [Fact]
    public async Task SaveChanges_NewEntity_CreatedAtEqualsUpdatedAt()
    {
        // Arrange
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Jane Smith",
            Email = "jane@example.com"
        };

        // Act
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Assert - on creation, both timestamps should be equal
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded!.CreatedAt.Should().Be(loaded.UpdatedAt!.Value);
    }

    [Fact]
    public async Task SaveChanges_UpdatedEntity_UpdatesOnlyUpdatedAt()
    {
        // Arrange
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Bob Wilson",
            Email = "bob@example.com"
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var createdAt = customer.CreatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(50);

        // Act - update the entity
        customer.FullName = "Robert Wilson";
        _db.Entry(customer).State = EntityState.Modified;
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded!.CreatedAt.Should().Be(createdAt); // CreatedAt should NOT change
        loaded.UpdatedAt.Should().BeOnOrAfter(createdAt); // UpdatedAt should be updated
    }

    [Fact]
    public async Task SaveChanges_MultipleNewEntities_AllGetAuditFields()
    {
        // Arrange
        var customers = new[]
        {
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Alice", Email = "alice@test.com" },
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Bob", Email = "bob@test.com" },
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Charlie", Email = "charlie@test.com" }
        };

        // Act
        _db.Customers.AddRange(customers);
        await _db.SaveChangesAsync();

        // Assert
        var allCustomers = await _db.Customers.ToListAsync();
        allCustomers.Should().HaveCount(3);
        allCustomers.Should().AllSatisfy(c =>
        {
            c.CreatedAt.Should().NotBe(default);
            c.UpdatedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public void SaveChanges_Sync_AlsoSetsAuditFields()
    {
        // Arrange - test synchronous SaveChanges path
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Sync Test",
            Email = "sync@test.com"
        };

        // Act
        _db.Customers.Add(customer);
        _db.SaveChanges();

        // Assert
        customer.CreatedAt.Should().NotBe(default);
        customer.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveChanges_NonAuditableEntity_DoesNotThrow()
    {
        // Arrange - TestProduct does NOT implement IAuditable
        var product = new TestProduct
        {
            PersistenceId = Guid7.New(),
            Name = "Simple Product",
            Price = 9.99m
        };

        // Act & Assert - saving a non-auditable entity should work fine
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var loaded = await _db.Products.FindAsync(product.PersistenceId);
        loaded.Should().NotBeNull();
    }
}
