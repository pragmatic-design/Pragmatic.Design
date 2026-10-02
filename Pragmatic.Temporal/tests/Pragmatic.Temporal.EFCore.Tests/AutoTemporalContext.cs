using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context configured ONLY via UsePragmaticTemporal() — no manual mapping.</summary>
public class AutoTemporalContext(DbContextOptions<AutoTemporalContext> options) : DbContext(options)
{
    public DbSet<TemporalEntity> Entities => Set<TemporalEntity>();
}
