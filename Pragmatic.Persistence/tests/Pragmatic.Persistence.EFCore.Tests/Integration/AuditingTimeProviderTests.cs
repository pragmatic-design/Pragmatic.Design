using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Tests that auditing respects the injected TimeProvider for deterministic timestamps.
///     Validates that IClock/TimeProvider is the standard timestamp contract.
/// </summary>
public class AuditingTimeProviderTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestDbContext _db;

    public AuditingTimeProviderTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 3, 15, 14, 30, 0, TimeSpan.Zero));
        _db = TestDbContextFactory.Create(_timeProvider);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SaveChanges_NewEntity_UsesTimeProvider()
    {
        // Arrange
        var fixedTime = _timeProvider.GetUtcNow();
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "TimeProvider Test",
            Email = "tp@test.com"
        };

        // Act
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Assert - timestamps should match the fake time provider exactly
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedAt.Should().Be(fixedTime);
        loaded.UpdatedAt.Should().Be(fixedTime);
    }

    [Fact]
    public async Task SaveChanges_UpdatedEntity_UsesAdvancedTime()
    {
        // Arrange
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "TimeProvider Update Test",
            Email = "tp-update@test.com"
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var createTime = _timeProvider.GetUtcNow();

        // Advance time by 1 hour
        _timeProvider.Advance(TimeSpan.FromHours(1));
        var updateTime = _timeProvider.GetUtcNow();

        // Act
        customer.FullName = "Updated Name";
        _db.Entry(customer).State = EntityState.Modified;
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded!.CreatedAt.Should().Be(createTime); // CreatedAt preserved
        loaded.UpdatedAt.Should().Be(updateTime);  // UpdatedAt uses advanced time
    }

    [Fact]
    public async Task SaveChanges_MultipleEntities_AllGetSameTimestamp()
    {
        // Arrange - all entities saved in same batch should get same timestamp
        var fixedTime = _timeProvider.GetUtcNow();
        var customers = new[]
        {
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Alice", Email = "alice@test.com" },
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Bob", Email = "bob@test.com" },
            new TestCustomer { PersistenceId = Guid7.New(), FullName = "Charlie", Email = "charlie@test.com" }
        };

        // Act
        _db.Customers.AddRange(customers);
        await _db.SaveChangesAsync();

        // Assert - all should have the exact same timestamp
        var allCustomers = await _db.Customers.ToListAsync();
        allCustomers.Should().AllSatisfy(c =>
        {
            c.CreatedAt.Should().Be(fixedTime);
            c.UpdatedAt.Should().Be(fixedTime);
        });
    }

    [Fact]
    public void SaveChanges_Sync_AlsoUsesTimeProvider()
    {
        // Arrange
        var fixedTime = _timeProvider.GetUtcNow();
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Sync TimeProvider Test",
            Email = "sync-tp@test.com"
        };

        // Act
        _db.Customers.Add(customer);
        _db.SaveChanges();

        // Assert
        customer.CreatedAt.Should().Be(fixedTime);
        customer.UpdatedAt.Should().Be(fixedTime);
    }

    [Fact]
    public async Task SaveChanges_WithDefaultTimeProvider_StillWorks()
    {
        // Arrange - no custom time provider, should fall back to system
        using var defaultDb = TestDbContextFactory.Create();
        var before = DateTimeOffset.UtcNow;

        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Default TimeProvider",
            Email = "default@test.com"
        };

        // Act
        defaultDb.Customers.Add(customer);
        await defaultDb.SaveChangesAsync();

        var after = DateTimeOffset.UtcNow;

        // Assert - should be within the time window
        var loaded = await defaultDb.Customers.FindAsync(customer.PersistenceId);
        loaded!.CreatedAt.Should().BeOnOrAfter(before);
        loaded.CreatedAt.Should().BeOnOrBefore(after);
    }
}
