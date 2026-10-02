using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.Identifiers;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Tests for <see cref="AuditingInterceptor" /> with TimeProvider support.
///     Validates that the interceptor correctly populates audit fields using the injected TimeProvider.
/// </summary>
public class AuditingInterceptorTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestDbContext _db;

    public AuditingInterceptorTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 10, 9, 0, 0, TimeSpan.Zero));
        _db = TestDbContextFactory.CreateWithAuditingInterceptor(_timeProvider);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Interceptor_NewEntity_SetsTimestampsFromTimeProvider()
    {
        // Arrange
        var fixedTime = _timeProvider.GetUtcNow();
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Interceptor Test",
            Email = "interceptor@test.com"
        };

        // Act
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedAt.Should().Be(fixedTime);
        loaded.UpdatedAt.Should().Be(fixedTime);
    }

    [Fact]
    public async Task Interceptor_UpdatedEntity_OnlyUpdatesUpdatedAt()
    {
        // Arrange
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Original Name",
            Email = "update-test@test.com"
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var createTime = customer.CreatedAt;

        // Advance time
        _timeProvider.Advance(TimeSpan.FromMinutes(30));

        // Act
        customer.FullName = "Updated Name";
        _db.Entry(customer).State = EntityState.Modified;
        await _db.SaveChangesAsync();

        // Assert
        customer.CreatedAt.Should().Be(createTime);
        customer.UpdatedAt.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public void Interceptor_Constructor_ThrowsOnNullTimeProvider()
    {
        var act = () => new AuditingInterceptor(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("timeProvider");
    }

    [Fact]
    public async Task Interceptor_DefaultConstructor_UsesSystemTimeProvider()
    {
        // Arrange - use default constructor (TimeProvider.System)
        var interceptor = new AuditingInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;

        using var db = new TestDbContext(options);
        db.Database.EnsureCreated();

        var before = DateTimeOffset.UtcNow;
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "System Clock Test",
            Email = "system@test.com"
        };

        // Act
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        // Assert
        customer.CreatedAt.Should().BeOnOrAfter(before);
        customer.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task Interceptor_NonAuditableEntity_IsIgnored()
    {
        // Arrange - TestProduct does NOT implement IAuditable
        var product = new TestProduct
        {
            PersistenceId = Guid7.New(),
            Name = "Test Product",
            Price = 19.99m
        };

        // Act & Assert - should not throw
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var loaded = await _db.Products.FindAsync(product.PersistenceId);
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task Interceptor_WithCurrentUser_SetsCreatedByOnInsert()
    {
        // Arrange
        var currentUser = FakeCurrentUser.Authenticated("user-42", "Jane Doe");
        using var db = TestDbContextFactory.CreateWithAuditingInterceptor(_timeProvider, currentUser);

        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "CreatedBy Test",
            Email = "createdby@test.com"
        };

        // Act
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Assert
        var loaded = await db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().Be("user-42");
        loaded.UpdatedBy.Should().Be("user-42");
    }

    [Fact]
    public async Task Interceptor_WithCurrentUser_SetsUpdatedByOnUpdate()
    {
        // Arrange — insert then update with the same user; verify UpdatedBy is set on modify
        var currentUser = FakeCurrentUser.Authenticated("user-42");
        var dbName = $"TestDb_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new AuditingInterceptor(_timeProvider, currentUser))
            .Options;

        using var db = new TestDbContext(options, _timeProvider);
        db.Database.EnsureCreated();

        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Original",
            Email = "updatedby@test.com"
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var createTime = customer.CreatedAt;
        customer.CreatedBy.Should().Be("user-42");

        // Advance time
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        // Act — modify the entity
        customer.FullName = "Updated";
        db.Entry(customer).State = EntityState.Modified;
        await db.SaveChangesAsync();

        // Assert — CreatedBy preserved, UpdatedBy set on modify
        customer.CreatedAt.Should().Be(createTime);
        customer.CreatedBy.Should().Be("user-42");
        customer.UpdatedBy.Should().Be("user-42");
        customer.UpdatedAt.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task Interceptor_WithoutCurrentUser_LeavesCreatedByAndUpdatedByNull()
    {
        // Arrange — no ICurrentUser provided (backward compat)
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "No User Test",
            Email = "nouser@test.com"
        };

        // Act — _db was created without ICurrentUser
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().BeNull();
        loaded.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public async Task Interceptor_WithAnonymousUser_LeavesCreatedByAndUpdatedByNull()
    {
        // Arrange — anonymous user (IsAuthenticated = false) should yield null via IdOrNull()
        var anonymousUser = AnonymousUser.Instance;
        using var db = TestDbContextFactory.CreateWithAuditingInterceptor(_timeProvider, anonymousUser);

        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Anonymous Test",
            Email = "anonymous@test.com"
        };

        // Act
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Assert — anonymous user's IdOrNull() returns null
        var loaded = await db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().BeNull();
        loaded.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public async Task Interceptor_WithoutCurrentUser_PreservesExistingAttributionOnUpdate()
    {
        // The case the framework actually hits: a row created by a signed-in user is later modified by
        // a worker, a job or a CLI, none of which supply an ICurrentUser. Attribution must survive that
        // save — writing null over it would not record "unknown", it would erase who did the work.
        // (The sibling tests asserting null attribution insert a fresh row whose columns are already
        // null, so they hold whether or not the assignment is guarded; this one does not.)
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Created By A Human",
            Email = "preserve-update@test.com",
            CreatedBy = "user-42",
            UpdatedBy = "user-42",
        };

        // _db has no ICurrentUser. Insert with attribution already populated...
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // ...then modify it the way a background worker would.
        customer.FullName = "Touched By A Worker";
        _db.Entry(customer).State = EntityState.Modified;
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().Be("user-42", "CreatedBy is written once, on insert");
        loaded.UpdatedBy.Should().Be("user-42", "an unknown user must not overwrite the last known one");
        loaded.UpdatedAt.Should().Be(_timeProvider.GetUtcNow(), "the timestamp is still a fact about the save");
    }

    [Fact]
    public async Task Interceptor_WithoutCurrentUser_PreservesCallerSuppliedAttributionOnInsert()
    {
        // An import or a backfill attributes rows to their original author explicitly. With no
        // ICurrentUser to override it, that value must reach the database.
        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Imported Row",
            Email = "preserve-insert@test.com",
            CreatedBy = "legacy-import",
            UpdatedBy = "legacy-import",
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().Be("legacy-import");
        loaded.UpdatedBy.Should().Be("legacy-import");
        loaded.CreatedAt.Should().Be(_timeProvider.GetUtcNow(), "timestamps are always the interceptor's");
    }

    [Fact]
    public async Task Interceptor_WithAnonymousUser_PreservesExistingAttribution()
    {
        // An anonymous ICurrentUser yields null from IdOrNull(), which must behave like no user at all
        // rather than like a user named "nobody".
        using var db = TestDbContextFactory.CreateWithAuditingInterceptor(_timeProvider, AnonymousUser.Instance);

        var customer = new TestCustomer
        {
            PersistenceId = Guid7.New(),
            FullName = "Anonymous Touch",
            Email = "preserve-anon@test.com",
            CreatedBy = "user-7",
            UpdatedBy = "user-7",
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        customer.FullName = "Touched Anonymously";
        db.Entry(customer).State = EntityState.Modified;
        await db.SaveChangesAsync();

        var loaded = await db.Customers.FindAsync(customer.PersistenceId);
        loaded.Should().NotBeNull();
        loaded!.CreatedBy.Should().Be("user-7");
        loaded.UpdatedBy.Should().Be("user-7");
    }

    [Fact]
    public void Interceptor_ThreeArgConstructor_ThrowsOnNullTimeProvider()
    {
        // The three-arg constructor should still enforce non-null TimeProvider
        var act = () => new AuditingInterceptor(null!, FakeCurrentUser.Authenticated("user-1"));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("timeProvider");
    }

    [Fact]
    public void Interceptor_ThreeArgConstructor_AllowsNullCurrentUser()
    {
        // Passing null for ICurrentUser is explicitly allowed (backward compat)
        var interceptor = new AuditingInterceptor(TimeProvider.System, null);
        interceptor.Should().NotBeNull();
    }
}
