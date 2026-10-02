using Microsoft.EntityFrameworkCore;
using Pragmatic.Events.EFCore;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     Tests for <see cref="LifecycleEventsInterceptor" />: the mapping from the tracked
///     <see cref="EntityState" /> to the <see cref="EntityLifecycle" /> the entity is asked to raise.
///     The interesting case is the soft delete, which reaches EF as a <see cref="EntityState.Modified" />
///     entry and must be told apart from an ordinary update.
/// </summary>
public class LifecycleEventsInterceptorTests
{
    [Fact]
    public async Task SavingChanges_WithAddedEntity_RaisesCreated()
    {
        await using var context = CreateContext();

        var entity = new SoftDeletableTestEntity { Name = "Initial" };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        entity.RaisedLifecycles.Should().Equal(EntityLifecycle.Created);
    }

    [Fact]
    public async Task SavingChanges_WithPlainUpdate_RaisesUpdated()
    {
        await using var context = CreateContext();

        var entity = new SoftDeletableTestEntity { Name = "Initial" };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();
        entity.RaisedLifecycles.Clear();

        entity.Name = "Renamed";
        await context.SaveChangesAsync();

        entity.RaisedLifecycles.Should().Equal(EntityLifecycle.Updated);
    }

    [Fact]
    public async Task SavingChanges_WhenIsDeletedFlipsFromFalseToTrue_RaisesDeleted()
    {
        await using var context = CreateContext();

        var entity = new SoftDeletableTestEntity { Name = "Initial" };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();
        entity.RaisedLifecycles.Clear();

        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        entity.RaisedLifecycles.Should().Equal(EntityLifecycle.Deleted);
    }

    [Fact]
    public async Task SavingChanges_WhenIsDeletedWasAlreadyTrue_RaisesUpdatedNotDeleted()
    {
        // The flag being *modified and true* is not a soft delete — only the transition
        // false -> true is. An already-deleted row whose IsDeleted is written again (a repository that
        // marks the whole entity modified, a re-attached graph) must not re-raise the Deleted event.
        await using var context = CreateContext();

        var entity = new SoftDeletableTestEntity { Name = "Initial", IsDeleted = true };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();
        entity.RaisedLifecycles.Clear();

        entity.Name = "Renamed while already soft-deleted";
        context.Entry(entity).Property(e => e.IsDeleted).IsModified = true;
        await context.SaveChangesAsync();

        entity.RaisedLifecycles.Should().Equal(EntityLifecycle.Updated);
    }

    private static LifecycleTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LifecycleTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(new LifecycleEventsInterceptor())
            .Options;

        return new LifecycleTestDbContext(options);
    }
}
