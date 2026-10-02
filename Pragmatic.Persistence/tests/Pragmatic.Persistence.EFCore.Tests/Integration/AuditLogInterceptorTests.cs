using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Auditing;
using Pragmatic.Persistence.Entity;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Tests for <see cref="AuditLogInterceptor"/> ([Audited] support): an append-only audit entry is
///     written, in the same SaveChanges, for each inserted/updated/deleted IAuditedEntity.
/// </summary>
public class AuditLogInterceptorTests
{
    private sealed class AuditedThing : IAuditedEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class PlainThing
    {
        public Guid Id { get; set; }
    }

    private sealed class SoftAuditedThing : IAuditedEntity, ISoftDelete
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    private sealed class AuditTestContext(DbContextOptions<AuditTestContext> options) : DbContext(options)
    {
        public DbSet<AuditedThing> Things => Set<AuditedThing>();
        public DbSet<PlainThing> PlainThings => Set<PlainThing>();
        public DbSet<SoftAuditedThing> SoftThings => Set<SoftAuditedThing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditedThing>(b => b.HasKey(e => e.Id));
            modelBuilder.Entity<PlainThing>(b => b.HasKey(e => e.Id));
            modelBuilder.Entity<SoftAuditedThing>(b => b.HasKey(e => e.Id));
            Pragmatic.Audit.EFCore.AuditDbContext.ApplyAuditConfigurations(modelBuilder);
        }
    }

    private static AuditTestContext NewContext()
        => new(new DbContextOptionsBuilder<AuditTestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditLogInterceptor(TimeProvider.System, null))
            .Options);

    [Fact]
    public async Task Insert_WritesCreatedAuditEntry()
    {
        using var ctx = NewContext();
        var thing = new AuditedThing { Id = Guid.NewGuid(), Name = "X" };
        ctx.Things.Add(thing);
        await ctx.SaveChangesAsync();

        var audit = ctx.Set<Pragmatic.Audit.AuditEntry>().Single();
        audit.Operation.Should().Be("Data.EntityCreated");
        audit.TargetType.Should().Be("AuditedThing");
        audit.TargetId.Should().Be(thing.Id.ToString());
    }

    [Fact]
    public async Task Update_WritesUpdatedAuditEntry()
    {
        using var ctx = NewContext();
        var thing = new AuditedThing { Id = Guid.NewGuid(), Name = "X" };
        ctx.Things.Add(thing);
        await ctx.SaveChangesAsync();

        thing.Name = "Y";
        await ctx.SaveChangesAsync();

        ctx.Set<Pragmatic.Audit.AuditEntry>().Select(a => a.Operation).Should().BeEquivalentTo(["Data.EntityCreated", "Data.EntityUpdated"]);
    }

    [Fact]
    public async Task NonAuditedEntity_WritesNoAuditEntry()
    {
        using var ctx = NewContext();
        ctx.PlainThings.Add(new PlainThing { Id = Guid.NewGuid() });
        await ctx.SaveChangesAsync();

        ctx.Set<Pragmatic.Audit.AuditEntry>().Should().BeEmpty();
    }
    [Fact]
    public async Task SoftDelete_WritesDeletedAuditEntry_NotUpdated()
    {
        using var ctx = NewContext();
        var thing = new SoftAuditedThing { Id = Guid.NewGuid() };
        ctx.SoftThings.Add(thing);
        await ctx.SaveChangesAsync();

        // A soft delete reaches EF as Modified — the audit trail must record the DOMAIN action.
        thing.IsDeleted = true;
        thing.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();

        ctx.Set<Pragmatic.Audit.AuditEntry>().AsEnumerable().Select(a => a.Operation)
            .Should().Contain("Data.EntityDeleted").And.NotContain("Data.EntityUpdated");

        // And a restore records "Restored".
        thing.IsDeleted = false;
        thing.DeletedAt = null;
        await ctx.SaveChangesAsync();

        ctx.Set<Pragmatic.Audit.AuditEntry>().AsEnumerable().Select(a => a.Operation)
            .Should().Contain("Data.EntityRestored");
    }
}
