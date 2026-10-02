using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.DataOwnership;

/// <summary>
///     Dedicated DbContext for the Data Ownership scenarios — separate from
///     <c>SampleDbContext</c> so the OwnedNote / ScopedProject entities don't
///     pollute the other demos. Uses EF Core InMemory for portability.
/// </summary>
public sealed class OwnershipDbContext(DbContextOptions<OwnershipDbContext> options) : DbContext(options)
{
    public DbSet<OwnedNote> Notes => Set<OwnedNote>();
    public DbSet<ScopedProject> Projects => Set<ScopedProject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OwnedNote>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(200).IsRequired();
            // OwnerId is added by the SG as part of [HasOwner]. No extra
            // configuration needed beyond what the provider infers.
        });

        modelBuilder.Entity<ScopedProject>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            // AccessScopes is a List<string> the SG adds; EF InMemory handles
            // reference-type collections natively without extra conversion.
        });
    }
}
