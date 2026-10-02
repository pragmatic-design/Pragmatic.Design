using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context with UsePragmaticTemporal(ApplyToAllProperties = false) and no manual mapping.</summary>
public class OptOutContext(DbContextOptions<OptOutContext> options) : DbContext(options)
{
    public DbSet<OptOutEntity> Entities => Set<OptOutEntity>();
}
