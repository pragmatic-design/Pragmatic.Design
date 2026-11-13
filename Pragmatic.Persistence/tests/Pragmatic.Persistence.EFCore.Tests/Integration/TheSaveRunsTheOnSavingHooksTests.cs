using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Identifiers;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The EF Core save runs an entity's <see cref="IEntityLifecycle{T}.OnSaving" /> hook, whatever wrote it (#644).
/// </summary>
/// <remarks>
///     <para>
///         The hook ran only in the mutation invoker, so a domain action, a job or a seed saved past it and a value derived on save was stale.
///         Here nothing but a context and a save: the path every one of those takes.
///     </para>
///     <para>
///         With the interceptor's runner as the generated registration builds it: the adapter per entity, the hooks resolved from the scope.
///     </para>
/// </remarks>
public sealed class TheSaveRunsTheOnSavingHooksTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly SlugHook _hook = new();
    private readonly ServiceProvider _services;
    private readonly HookedDbContext _db;

    public TheSaveRunsTheOnSavingHooksTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<IEntityLifecycle<HookedArticle>>(_hook)
            .AddSingleton<IEntitySavingHooks, EntitySavingHooks<HookedArticle>>()
            .AddSingleton<IEntitySavingHooks, EntitySavingHooks<HookedNote>>()
            .BuildServiceProvider();

        var runner = new EntitySavingHooksRunner(_services, _services.GetServices<IEntitySavingHooks>());
        var options = new DbContextOptionsBuilder<HookedDbContext>()
            .UseInMemoryDatabase($"OnSaving_{Guid.NewGuid():N}")
            .AddInterceptors(new EntitySavingHooksInterceptor(runner, _clock))
            .Options;

        _db = new HookedDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task AnAddedEntity_IsSavedWithWhatItsHookDerived()
    {
        var article = new HookedArticle { PersistenceId = Guid7.New(), Title = "Hello World" };
        _db.Articles.Add(article);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Articles.SingleAsync()).Slug.Should().Be("hello-world");
    }

    [Fact]
    public async Task AChangedEntity_IsSavedWithWhatItsHookDerived()
    {
        var article = new HookedArticle { PersistenceId = Guid7.New(), Title = "Hello World" };
        _db.Articles.Add(article);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.Articles.SingleAsync();
        loaded.Title = "Second Draft";
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Articles.SingleAsync()).Slug.Should().Be("second-draft", "the slug was derived after the change was detected");
    }

    /// <summary>Once per save: the save is the hook's only call site now, so a mutation over it runs it once too.</summary>
    [Fact]
    public async Task TheHook_RunsOncePerSave()
    {
        _db.Articles.Add(new HookedArticle { PersistenceId = Guid7.New(), Title = "Once" });
        await _db.SaveChangesAsync();

        _hook.Calls.Should().Be(1);
    }

    /// <summary>The control: an entity whose type has no hook, and an unchanged one, are left alone.</summary>
    [Fact]
    public async Task AnEntityWithoutAHook_OrUnchanged_IsLeftAlone()
    {
        _db.Articles.Add(new HookedArticle { PersistenceId = Guid7.New(), Title = "Kept" });
        await _db.SaveChangesAsync();
        _hook.Calls.Should().Be(1);

        _db.Notes.Add(new HookedNote { PersistenceId = Guid7.New(), Text = "Not An Article" });
        await _db.SaveChangesAsync();

        _hook.Calls.Should().Be(1, "the article was unchanged in the second save, and a note has no hook");
    }

    /// <summary>The synchronous save takes the same path.</summary>
    [Fact]
    public void TheSynchronousSave_RunsTheHookToo()
    {
        _db.Articles.Add(new HookedArticle { PersistenceId = Guid7.New(), Title = "Sync Save" });
        _db.SaveChanges();

        _db.Articles.AsNoTracking().Single().Slug.Should().Be("sync-save");
    }

    private sealed class SlugHook : IEntityLifecycle<HookedArticle>
    {
        public int Calls { get; private set; }

        public void OnSaving(HookedArticle entity, LifecycleContext context)
        {
            Calls++;
            entity.Slug = entity.Title.ToLowerInvariant().Replace(' ', '-');
        }
    }
}

/// <summary>An entity with a value derived on save.</summary>
public class HookedArticle : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
}

/// <summary>An entity with an adapter registered and no hook.</summary>
public class HookedNote : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A context of its own, so the shared corpus of this suite is untouched.</summary>
public class HookedDbContext(DbContextOptions<HookedDbContext> options) : DbContext(options)
{
    public DbSet<HookedArticle> Articles { get; set; } = null!;
    public DbSet<HookedNote> Notes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<HookedArticle>(b => b.HasKey(e => e.PersistenceId));
        modelBuilder.Entity<HookedNote>(b => b.HasKey(e => e.PersistenceId));
    }
}
