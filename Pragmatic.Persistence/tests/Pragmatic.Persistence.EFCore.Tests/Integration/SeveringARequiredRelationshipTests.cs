using Microsoft.EntityFrameworkCore;
using Pragmatic.Mapping.EFCore.Mutation;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Identifiers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Severing a required relationship orphans the dependent, and EF deletes an orphan. Whether that
///     deletion passes through the soft-delete mark is the question.
/// </summary>
/// <remarks>
///     <para>
///         It is the same promise on a second door. A soft-deletable child removed from a
///         <em>collection</em> is severed the same way and is marked, not destroyed — measured in
///         <c>SoftDeleteInterceptorTests</c>, written for exactly that. A single navigation set to
///         null under <c>[ReferenceStrategy(Detach)]</c> reaches the same place by a different route,
///         and the row is what the guarantee is about.
///     </para>
///     <para>
///         Deliberately below the generator: <c>MapOneToOne</c>'s null branch is one line —
///         <c>setter(null)</c> — so what happens to the row is decided by EF and the interceptor, not
///         by the emitted code. Testing it through a generated mutation would measure the same thing
///         with more moving parts between the assertion and the answer.
///     </para>
/// </remarks>
public sealed class SeveringARequiredRelationshipTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SeveringDbContext _db;

    public SeveringARequiredRelationshipTests()
    {
        var options = new DbContextOptionsBuilder<SeveringDbContext>()
            .UseInMemoryDatabase($"Severing_{Guid.NewGuid():N}")
            .AddInterceptors(new SoftDeleteInterceptor(_clock))
            .Options;

        _db = new SeveringDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private async Task<SeveringStory> AStoryWithAFrameAsync()
    {
        var story = new SeveringStory { PersistenceId = Guid7.New(), Title = "A story" };
        story.Frame = new SeveringFrame { PersistenceId = Guid7.New(), Caption = "A frame", Story = story };

        _db.Stories.Add(story);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        return story;
    }

    /// <summary>Detaching a recoverable dependent marks the row instead of destroying it.</summary>
    /// <remarks>
    ///     Through <c>MapOneToOne</c>, which is the door <c>[ReferenceStrategy(Detach)]</c> generates.
    ///     Setting the navigation to null by hand measures EF and the interceptor, and that layer was
    ///     measured while diagnosing this: the tracker holds the dependent as <c>Deleted</c> before the
    ///     save, the interceptor flips it and sets the flag, and the row is gone anyway — the link is
    ///     still severed and a required key cannot be null, so EF orphans it again.
    /// </remarks>
    [Fact]
    public async Task DetachingASoftDeletableDependent_MarksTheRowRatherThanDestroyingIt()
    {
        var story = await AStoryWithAFrameAsync();
        var frameId = story.Frame!.PersistenceId;

        DetachFrame(story);
        await _db.SaveChangesAsync();

        var frame = await _db.Frames.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.PersistenceId == frameId);

        frame.Should().NotBeNull("[SoftDelete] promises the row stays, whichever door removed it");
        frame!.IsDeleted.Should().BeTrue();
        frame.DeletedAt.Should().Be(_clock.GetUtcNow(), "the instant is the interceptor's, on every path");
    }

    /// <summary>And no reader sees a frame any more, which is what detaching asked for.</summary>
    /// <remarks>
    ///     The control on the assertion above: "the row is still there" is also true of a detach that
    ///     did nothing, and a merge that ignored the null would satisfy it. Read <b>through</b> the
    ///     filter, deliberately: under a required relationship the key cannot be null, so "no frame"
    ///     is expressed by the row being invisible rather than by a dangling link.
    /// </remarks>
    [Fact]
    public async Task NoReaderSeesAFrame_WhichIsWhatDetachingAsked()
    {
        var story = await AStoryWithAFrameAsync();

        DetachFrame(story);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var reloaded = await _db.Stories
            .Include(s => s.Frame)
            .FirstAsync(s => s.PersistenceId == story.PersistenceId);

        reloaded.Frame.Should().BeNull("the null under Detach means: this story has no frame");
    }

    /// <summary>
    ///     The second control: a dependent that is <em>not</em> soft-deletable is still removed.
    /// </summary>
    /// <remarks>
    ///     What happens to the row is the relationship's business, not the framework's — the same
    ///     division of labour a collection has always had. Without this, "the row survives" would be
    ///     satisfied by a detach that stopped removing anything, leaving a row nobody can reach behind
    ///     every severed link.
    /// </remarks>
    [Fact]
    public async Task ADependentThatIsNotSoftDeletable_IsStillRemoved()
    {
        var story = new SeveringStory { PersistenceId = Guid7.New(), Title = "Plain" };
        story.Note = new SeveringNote { PersistenceId = Guid7.New(), Body = "A note", Story = story };
        _db.Stories.Add(story);
        await _db.SaveChangesAsync();
        var noteId = story.Note.PersistenceId;

        EfMutationHelpers.MapOneToOne<SeveringStory, object, SeveringNote>(
            null,
            _db.Entry(story).Reference(s => s.Note),
            v => story.Note = v,
            _ => throw new global::System.InvalidOperationException("the factory is not reached by a null"));

        await _db.SaveChangesAsync();

        (await _db.Notes.FirstOrDefaultAsync(n => n.PersistenceId == noteId))
            .Should().BeNull("nothing said this row was recoverable");
    }

    /// <summary>The detach the generator emits for <c>[ReferenceStrategy(Detach)]</c>.</summary>
    private void DetachFrame(SeveringStory story)
        => EfMutationHelpers.MapOneToOne<SeveringStory, object, SeveringFrame>(
            null,
            _db.Entry(story).Reference(s => s.Frame),
            v => story.Frame = v,
            _ => throw new global::System.InvalidOperationException("the factory is not reached by a null"));
}

/// <summary>The principal of both relationships under test.</summary>
public class SeveringStory : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Title { get; set; } = "";
    public SeveringFrame? Frame { get; set; }
    public SeveringNote? Note { get; set; }
}

/// <summary>A soft-deletable dependent whose key to the story is required.</summary>
public class SeveringFrame : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string Caption { get; set; } = "";
    public Guid StoryId { get; set; }
    public SeveringStory Story { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>The same shape, without the promise.</summary>
public class SeveringNote : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Body { get; set; } = "";
    public Guid StoryId { get; set; }
    public SeveringStory Story { get; set; } = null!;
}

/// <summary>A context of its own, so the shared corpus of this suite is untouched.</summary>
public class SeveringDbContext(DbContextOptions<SeveringDbContext> options) : DbContext(options)
{
    public DbSet<SeveringStory> Stories { get; set; } = null!;
    public DbSet<SeveringFrame> Frames { get; set; } = null!;
    public DbSet<SeveringNote> Notes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SeveringStory>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Title).IsRequired();
        });

        modelBuilder.Entity<SeveringFrame>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Caption).IsRequired();
            // Required: severing the link leaves an orphan EF must do something with, which is the
            // whole subject of these tests.
            b.HasOne(e => e.Story).WithOne(s => s.Frame).HasForeignKey<SeveringFrame>(e => e.StoryId)
                .IsRequired();
            b.HasQueryFilter(e => !e.IsDeleted);
        });

        modelBuilder.Entity<SeveringNote>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Body).IsRequired();
            b.HasOne(e => e.Story).WithOne(s => s.Note).HasForeignKey<SeveringNote>(e => e.StoryId)
                .IsRequired();
        });
    }
}
