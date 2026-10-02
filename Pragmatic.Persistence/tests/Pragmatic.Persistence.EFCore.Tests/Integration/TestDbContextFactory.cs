using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Interceptors;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Factory for creating in-memory TestDbContext instances for integration tests.
///     Each call creates an isolated database with a unique name.
/// </summary>
internal static class TestDbContextFactory
{
    /// <summary>
    ///     Creates a new TestDbContext backed by InMemory provider.
    ///     Each invocation uses a unique database name for test isolation.
    /// </summary>
    public static TestDbContext Create(TimeProvider? timeProvider = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid():N}")
            .Options;

        var context = new TestDbContext(options, timeProvider);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Creates a new TestDbContext with the AuditingInterceptor using a custom TimeProvider.
    /// </summary>
    public static TestDbContext CreateWithAuditingInterceptor(TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid():N}")
            .AddInterceptors(new AuditingInterceptor(tp))
            .Options;

        var context = new TestDbContext(options, tp);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Creates a new TestDbContext with the AuditingInterceptor using a custom TimeProvider
    ///     and an <see cref="ICurrentUser" /> for populating CreatedBy/UpdatedBy fields.
    /// </summary>
    public static TestDbContext CreateWithAuditingInterceptor(
        TimeProvider timeProvider,
        ICurrentUser? currentUser)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid():N}")
            .AddInterceptors(new AuditingInterceptor(timeProvider, currentUser))
            .Options;

        var context = new TestDbContext(options, timeProvider);
        context.Database.EnsureCreated();
        return context;
    }
}
