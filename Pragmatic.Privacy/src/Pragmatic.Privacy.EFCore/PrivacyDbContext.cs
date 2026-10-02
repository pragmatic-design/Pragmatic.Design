using Microsoft.EntityFrameworkCore;
using Pragmatic.Privacy.EFCore.Entities;

namespace Pragmatic.Privacy.EFCore;

/// <summary>
///     Holds the subject registry: the mapping between people and the references the rest of the system
///     knows them by.
/// </summary>
public class PrivacyDbContext(DbContextOptions<PrivacyDbContext> options) : DbContext(options)
{
    /// <summary>Subjects, live and erased.</summary>
    public DbSet<SubjectRecord> Subjects => Set<SubjectRecord>();

    /// <summary>Consent records, active and withdrawn — a withdrawn one is still evidence.</summary>
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyPrivacyConfigurations(modelBuilder);
    }

    /// <summary>
    ///     Applies the registry's entity configuration to the given model builder, for consumers keeping
    ///     these tables alongside their own data.
    /// </summary>
    /// <remarks>
    ///     Prefer this over registering a second context against the same database: <c>EnsureCreated</c>
    ///     is all-or-nothing per database, so the second context would find the database already there
    ///     and silently create none of its tables.
    /// </remarks>
    public static void ApplyPrivacyConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SubjectRecordEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentRecordEntityTypeConfiguration());
    }
}
