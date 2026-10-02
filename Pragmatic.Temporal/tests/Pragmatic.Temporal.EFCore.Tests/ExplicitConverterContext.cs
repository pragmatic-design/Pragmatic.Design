using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     A context that stores its instant as ticks, the way the audit log does.
/// </summary>
/// <remarks>
///     ⚠️ The shape that broke every write in an application: the UTC normalisation replaced this
///     converter, so a timestamptz went at a bigint column. The error named the column, not the
///     convention.
/// </remarks>
public class ExplicitConverterContext(DbContextOptions<ExplicitConverterContext> options) : DbContext(options)
{
    /// <summary>The converter this context chose, which nothing may take away.</summary>
    public static readonly ValueConverter<DateTimeOffset, long> Ticks =
        new(value => value.UtcTicks, stored => new DateTimeOffset(stored, TimeSpan.Zero));

    public DbSet<InstantEntity> Entities => Set<InstantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstantEntity>().Property(e => e.Moment).HasConversion(Ticks);
    }
}
