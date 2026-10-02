using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     Context for query-extension tests. Timestamp is stored as UTC ticks so
///     DateTimeOffset comparisons translate to SQL on Sqlite with chronological ordering.
/// </summary>
public class QueryContext(DbContextOptions<QueryContext> options) : DbContext(options)
{
    public DbSet<QueryEvent> Events => Set<QueryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueryEvent>()
            .Property(e => e.Timestamp)
            .HasConversion(new ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero)));
    }
}
