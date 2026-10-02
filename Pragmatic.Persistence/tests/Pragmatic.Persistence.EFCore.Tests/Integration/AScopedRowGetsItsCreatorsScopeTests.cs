using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.Entity;
using Xunit;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A row of a <c>[HasAccessScopes]</c> entity is inserted carrying the scope of whoever created it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Nothing wrote <c>AccessScopes</c> at all. The attribute generated the column, the
///         <c>GrantScope</c>/<c>RevokeScope</c> pair and a filter reading
///         <c>entity.AccessScopes.Any(s =&gt; userScopes.Contains(s))</c> — and no interceptor, invoker or
///         hook ever put anything in the list, so that filter was <b>false for every row and every
///         caller</b>. Only the bypass permission could see such a row, which is why nothing reported it:
///         the Showcase's default test permissions include the bypass.
///     </para>
///     <para>
///         This is the same move <see cref="OwnershipInterceptor" /> made for <c>[HasOwner]</c>, and its
///         remarks describe the identical failure. Ownership got its write side; scopes never did.
///     </para>
/// </remarks>
public class AScopedRowGetsItsCreatorsScopeTests
{
    /// <summary>An inserted row with no scopes carries the caller's own.</summary>
    [Fact]
    public async Task AnInsertedRow_WithNoScopes_CarriesTheCallersOwn()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var document = new ScopedDocument { Title = "Anything" };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        document.AccessScopes.Should().Contain("user:alice");
    }

    /// <summary>
    ///     The control: scopes set deliberately are left alone.
    /// </summary>
    /// <remarks>
    ///     Without it, "the row carries the caller's scope" is satisfied by an implementation that adds
    ///     it to every insert — including an import attributing rows to the team that owned them, or a
    ///     seed, where the caller is an operator and not the audience.
    /// </remarks>
    [Fact]
    public async Task AnInsertedRow_ThatAlreadyHasScopes_KeepsThem()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var document = new ScopedDocument { Title = "Imported" };
        document.AccessScopes.Add("scope:legal");
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        document.AccessScopes.Should().BeEquivalentTo(["scope:legal"],
            "a row whose audience was stated is not re-attributed to whoever ran the import");
    }

    /// <summary>
    ///     The second control: no principal, no scope — and it is not an error.
    /// </summary>
    /// <remarks>
    ///     A recurring job, a message off a bus, a startup seed. The row is the system's, and it has to
    ///     stay expressible: this is the branch that keeps "the caller's scope" from turning into
    ///     "whatever scope was lying around".
    /// </remarks>
    [Fact]
    public async Task AnInsertedRow_WithNoPrincipal_StaysUnscoped()
    {
        await using var db = Context(currentUser: null);

        var document = new ScopedDocument { Title = "From a job" };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        document.AccessScopes.Should().BeEmpty("nobody caused this row, so it belongs to the system");
    }

    /// <summary>The third control: an anonymous caller is no caller.</summary>
    [Fact]
    public async Task AnInsertedRow_WithAnAnonymousCaller_StaysUnscoped()
    {
        await using var db = Context(FakeCurrentUser.Anonymous());

        var document = new ScopedDocument { Title = "From the public site" };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        document.AccessScopes.Should().BeEmpty();
    }

    /// <summary>The fourth control: an update does not re-scope a row.</summary>
    /// <remarks>
    ///     Scoping happens once, at insert, exactly as ownership does — otherwise the last person to
    ///     touch a row would quietly acquire it, and everyone who could see it before would lose it.
    /// </remarks>
    [Fact]
    public async Task AnUpdatedRow_IsNotRescopedToWhoeverTouchedIt()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var document = new ScopedDocument { Title = "First" };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        document.AccessScopes.Clear();
        document.Title = "Second";
        await db.SaveChangesAsync();

        document.AccessScopes.Should().BeEmpty("only an insert is attributed");
    }

    private static ScopeTestContext Context(ICurrentUser? currentUser)
    {
        var options = new DbContextOptionsBuilder<ScopeTestContext>()
            .UseInMemoryDatabase($"ScopeDb_{Guid.NewGuid():N}")
            .AddInterceptors(new ScopeInterceptor(currentUser))
            .Options;

        var context = new ScopeTestContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>What the generator emits for <c>[HasAccessScopes]</c>, by hand.</summary>
    private sealed class ScopedDocument : IScopedEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title { get; set; } = string.Empty;

        public List<string> AccessScopes { get; } = [];
    }

    private sealed class ScopeTestContext(DbContextOptions<ScopeTestContext> options) : DbContext(options)
    {
        public DbSet<ScopedDocument> Documents => Set<ScopedDocument>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScopedDocument>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.PrimitiveCollection(d => d.AccessScopes);
            });
        }
    }
}
