using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography.EFCore.Entities;

namespace Pragmatic.Cryptography.EFCore;

/// <summary>
///     Holds the per-subject key table. Consumers that would rather keep these rows alongside their own
///     data can call <see cref="ApplyCryptographyConfigurations" /> from their own
///     <c>OnModelCreating</c> instead of using this context directly.
/// </summary>
public class CryptographyDbContext(DbContextOptions<CryptographyDbContext> options) : DbContext(options)
{
    /// <summary>Per-subject encryption keys, wrapped by the master key ring.</summary>
    public DbSet<SubjectKeyRecord> SubjectKeys => Set<SubjectKeyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyCryptographyConfigurations(modelBuilder);
    }

    /// <summary>
    ///     Applies the subject-key entity configuration to the given model builder.
    /// </summary>
    public static void ApplyCryptographyConfigurations(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new SubjectKeyEntityTypeConfiguration());
}
