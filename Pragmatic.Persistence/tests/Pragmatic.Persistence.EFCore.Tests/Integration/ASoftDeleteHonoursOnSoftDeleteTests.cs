using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.EFCore.SoftDelete;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Identifiers;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
using Pragmatic.Testing.Assertions;
using Xunit;
using DeleteBehavior = Pragmatic.Persistence.Entity.DeleteBehavior;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A soft delete applies the relation's <c>OnSoftDelete</c> when one is declared, and leaves the referrers
///     alone when none is (#617).
/// </summary>
/// <remarks>
///     <para>
///         A soft delete is an update, so the database's foreign key never sees it: a <c>Restrict</c> refused
///         nothing, and a record still in use was "deleted" with its referrers pointing at it. <c>OnDelete</c>
///         keeps meaning the physical delete, as Time off relies on; <c>OnSoftDelete</c> is the declaration for
///         the soft one.
///     </para>
///     <para>
///         Below the generator, with the rules put on the model by hand as the generated configuration puts them:
///         what is measured here is the interceptor. The generator's part is measured in
///         <c>OnSoftDeleteGeneratorTests</c>.
///     </para>
/// </remarks>
public sealed class ASoftDeleteHonoursOnSoftDeleteTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly OnSoftDeleteDbContext _db;

    public ASoftDeleteHonoursOnSoftDeleteTests()
    {
        var options = new DbContextOptionsBuilder<OnSoftDeleteDbContext>()
            .UseInMemoryDatabase($"OnSoftDelete_{Guid.NewGuid():N}")
            .AddInterceptors(new SoftDeleteInterceptor(_clock))
            .Options;

        _db = new OnSoftDeleteDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Restrict_RefusesTheSoftDelete_WhileALiveReferrerExists()
    {
        var shelf = await AShelfAsync();
        _db.Books.Add(new OsdBook { PersistenceId = Guid7.New(), Title = "Kept", ShelfId = shelf.PersistenceId });
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));
        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => _db.SaveChangesAsync());

        var inUse = refused.Error.Should().BeOfType<DbInUseError>().Subject;
        inUse.EntityType.Should().Be(nameof(OsdShelf));
        inUse.UsedBy.Should().Be(nameof(OsdBook));

        _db.ChangeTracker.Clear();
        (await _db.Shelves.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == shelf.PersistenceId))
            .IsDeleted.Should().BeFalse("a refused soft delete writes nothing");
    }

    /// <summary>The same refusal when the flag is raised by the caller, as a generated delete mutation does.</summary>
    [Fact]
    public async Task Restrict_RefusesAFlagRaisedByTheCaller_AsWellAsARemove()
    {
        var shelf = await AShelfAsync();
        _db.Books.Add(new OsdBook { PersistenceId = Guid7.New(), Title = "Kept", ShelfId = shelf.PersistenceId });
        await _db.SaveChangesAsync();

        var loaded = await FreshAsync(shelf);
        loaded.IsDeleted = true;
        loaded.DeletedAt = _clock.GetUtcNow();

        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => _db.SaveChangesAsync());
        refused.Error.Should().BeOfType<DbInUseError>();
    }

    /// <summary>The synchronous save takes the same path.</summary>
    [Fact]
    public async Task Restrict_RefusesOnASynchronousSave_Too()
    {
        var shelf = await AShelfAsync();
        _db.Books.Add(new OsdBook { PersistenceId = Guid7.New(), Title = "Kept", ShelfId = shelf.PersistenceId });
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));

        var refused = Assert.Throws<PersistenceRuleViolationException>(() => _db.SaveChanges());
        refused.Error.Should().BeOfType<DbInUseError>();
    }

    /// <summary>The control: a referrer already soft-deleted is not live, and does not hold the principal.</summary>
    [Fact]
    public async Task Restrict_LetsTheSoftDeleteThrough_WhenEveryReferrerIsDeleted()
    {
        var shelf = await AShelfAsync();
        var book = new OsdBook { PersistenceId = Guid7.New(), Title = "Gone", ShelfId = shelf.PersistenceId };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        _db.Books.Remove(book);
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Shelves.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == shelf.PersistenceId))
            .IsDeleted.Should().BeTrue();
    }

    /// <summary>A relation that declares no <c>OnSoftDelete</c> is left as it was: the shelf goes, the poster stays.</summary>
    [Fact]
    public async Task WithoutOnSoftDelete_TheSoftDeleteLeavesTheReferrersAsTheyAre()
    {
        var shelf = await AShelfAsync();
        var poster = new OsdPoster { PersistenceId = Guid7.New(), Caption = "Kept", ShelfId = shelf.PersistenceId };
        _db.Posters.Add(poster);
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Shelves.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == shelf.PersistenceId))
            .IsDeleted.Should().BeTrue();
        (await _db.Posters.SingleAsync(p => p.PersistenceId == poster.PersistenceId))
            .ShelfId.Should().Be(shelf.PersistenceId);
    }

    [Fact]
    public async Task SetNull_EmptiesTheReferrersKey_InTheSameSave()
    {
        var shelf = await AShelfAsync();
        var label = new OsdLabel { PersistenceId = Guid7.New(), Text = "Fiction", ShelfId = shelf.PersistenceId };
        _db.Labels.Add(label);
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Labels.SingleAsync(l => l.PersistenceId == label.PersistenceId)).ShelfId.Should().BeNull();
        (await _db.Shelves.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == shelf.PersistenceId))
            .IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Cascade_SoftDeletesTheReferrers_WithThePrincipalsInstant_AndTheirsInTurn()
    {
        var shelf = await AShelfAsync();
        var bin = new OsdBin { PersistenceId = Guid7.New(), Name = "Returns", ShelfId = shelf.PersistenceId };
        var slip = new OsdSlip { PersistenceId = Guid7.New(), Note = "Torn", BinId = bin.PersistenceId };
        _db.Bins.Add(bin);
        _db.Slips.Add(slip);
        await _db.SaveChangesAsync();

        _db.Shelves.Remove(await FreshAsync(shelf));
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var deletedShelf = await _db.Shelves.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == shelf.PersistenceId);
        var deletedBin = await _db.Bins.IgnoreQueryFilters().SingleAsync(b => b.PersistenceId == bin.PersistenceId);
        var deletedSlip = await _db.Slips.IgnoreQueryFilters().SingleAsync(s => s.PersistenceId == slip.PersistenceId);

        deletedBin.IsDeleted.Should().BeTrue();
        deletedBin.DeletedAt.Should().Be(deletedShelf.DeletedAt, "a restore brings back what carries the principal's instant");
        deletedSlip.IsDeleted.Should().BeTrue("the bin's own OnSoftDelete applies when the cascade reaches it");
        deletedSlip.DeletedAt.Should().Be(deletedShelf.DeletedAt);
    }

    private async Task<OsdShelf> AShelfAsync()
    {
        var shelf = new OsdShelf { PersistenceId = Guid7.New(), Name = "A shelf" };
        _db.Shelves.Add(shelf);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        return shelf;
    }

    /// <summary>
    ///     The shelf as a request finds it: loaded alone, with nothing else tracked. Removed beside its tracked
    ///     referrers, EF would apply the physical delete's behaviour to them in the tracker before any save.
    /// </summary>
    private async Task<OsdShelf> FreshAsync(OsdShelf shelf)
    {
        _db.ChangeTracker.Clear();
        return await _db.Shelves.SingleAsync(s => s.PersistenceId == shelf.PersistenceId).ConfigureAwait(false);
    }
}

/// <summary>The principal: a soft-deletable shelf, referenced four ways.</summary>
public class OsdShelf : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>Holds its shelf: <c>OnSoftDelete = Restrict</c>.</summary>
public class OsdBook : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string Title { get; set; } = "";
    public Guid ShelfId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>Points at its shelf with no <c>OnSoftDelete</c>.</summary>
public class OsdPoster : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Caption { get; set; } = "";
    public Guid ShelfId { get; set; }
}

/// <summary>Lets go of its shelf: <c>OnSoftDelete = SetNull</c>.</summary>
public class OsdLabel : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Text { get; set; } = "";
    public Guid? ShelfId { get; init; }
}

/// <summary>Goes with its shelf: <c>OnSoftDelete = Cascade</c>.</summary>
public class OsdBin : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string Name { get; set; } = "";
    public Guid ShelfId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>Goes with its bin: <c>OnSoftDelete = Cascade</c>, one level further.</summary>
public class OsdSlip : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string Note { get; set; } = "";
    public Guid BinId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>A context of its own, with the rules on the foreign keys as the generated configuration puts them.</summary>
public class OnSoftDeleteDbContext(DbContextOptions<OnSoftDeleteDbContext> options) : DbContext(options)
{
    public DbSet<OsdShelf> Shelves { get; set; } = null!;
    public DbSet<OsdBook> Books { get; set; } = null!;
    public DbSet<OsdPoster> Posters { get; set; } = null!;
    public DbSet<OsdLabel> Labels { get; set; } = null!;
    public DbSet<OsdBin> Bins { get; set; } = null!;
    public DbSet<OsdSlip> Slips { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OsdShelf>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<OsdBook>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasQueryFilter(e => !e.IsDeleted);
            b.HasOne<OsdShelf>().WithMany().HasForeignKey(e => e.ShelfId)
                .HasAnnotation(SoftDeleteReferrerRule.Annotation, new SoftDeleteReferrerRule<OsdShelf, OsdBook>(
                    DeleteBehavior.Restrict,
                    (db, p) => db.Set<OsdBook>().Where(d => d.ShelfId == p.PersistenceId)));
        });

        modelBuilder.Entity<OsdPoster>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasOne<OsdShelf>().WithMany().HasForeignKey(e => e.ShelfId);
        });

        modelBuilder.Entity<OsdLabel>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasOne<OsdShelf>().WithMany().HasForeignKey(e => e.ShelfId).IsRequired(false)
                .HasAnnotation(SoftDeleteReferrerRule.Annotation, new SoftDeleteReferrerRule<OsdShelf, OsdLabel>(
                    DeleteBehavior.SetNull,
                    (db, p) => db.Set<OsdLabel>().Where(d => d.ShelfId == p.PersistenceId)));
        });

        modelBuilder.Entity<OsdBin>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasQueryFilter(e => !e.IsDeleted);
            b.HasOne<OsdShelf>().WithMany().HasForeignKey(e => e.ShelfId)
                .HasAnnotation(SoftDeleteReferrerRule.Annotation, new SoftDeleteReferrerRule<OsdShelf, OsdBin>(
                    DeleteBehavior.Cascade,
                    (db, p) => db.Set<OsdBin>().Where(d => d.ShelfId == p.PersistenceId)));
        });

        modelBuilder.Entity<OsdSlip>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.HasQueryFilter(e => !e.IsDeleted);
            b.HasOne<OsdBin>().WithMany().HasForeignKey(e => e.BinId)
                .HasAnnotation(SoftDeleteReferrerRule.Annotation, new SoftDeleteReferrerRule<OsdBin, OsdSlip>(
                    DeleteBehavior.Cascade,
                    (db, p) => db.Set<OsdSlip>().Where(d => d.BinId == p.PersistenceId)));
        });
    }
}
