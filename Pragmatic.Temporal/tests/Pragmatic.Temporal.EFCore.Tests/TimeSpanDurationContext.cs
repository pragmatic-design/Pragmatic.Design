using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context using UsePragmaticTemporal with StoreDurationAsTicks = false.</summary>
public class TimeSpanDurationContext(DbContextOptions<TimeSpanDurationContext> options) : DbContext(options)
{
    public DbSet<TemporalEntity> Entities => Set<TemporalEntity>();
}
