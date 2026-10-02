using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context over <see cref="InstantEntity"/>, configured only by UsePragmaticTemporal().</summary>
public class InstantContext(DbContextOptions<InstantContext> options) : DbContext(options)
{
    public DbSet<InstantEntity> Entities => Set<InstantEntity>();
}
