using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore.Conventions;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context using the manual ApplyTemporalConventions path (no UsePragmaticTemporal).</summary>
public class ManualApplyContext(DbContextOptions<ManualApplyContext> options) : DbContext(options)
{
    public DbSet<TemporalEntity> Entities => Set<TemporalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyTemporalConventions();
    }
}
