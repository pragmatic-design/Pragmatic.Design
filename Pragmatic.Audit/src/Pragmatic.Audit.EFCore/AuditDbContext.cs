using Microsoft.EntityFrameworkCore;
using Pragmatic.Audit.EFCore.Entities;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Holds the trail: entries, the segments they are sealed into, and the gaps retention has left.
/// </summary>
public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    /// <summary>Append-only trail entries.</summary>
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    /// <summary>Segments, open and sealed.</summary>
    public DbSet<AuditSegment> Segments => Set<AuditSegment>();

    /// <summary>Declared gaps left by retention.</summary>
    public DbSet<PrunedRange> PrunedRanges => Set<PrunedRange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyAuditConfigurations(modelBuilder);
    }

    /// <summary>
    ///     Applies the trail's entity configurations to the given model builder, for consumers that
    ///     keep these tables alongside their own data.
    /// </summary>
    public static void ApplyAuditConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditEntryEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new AuditSegmentEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PrunedRangeEntityTypeConfiguration());
    }
}
