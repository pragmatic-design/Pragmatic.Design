using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>The same entity, with the UTC normalisation switched off.</summary>
public class OptOutInstantContext(DbContextOptions<OptOutInstantContext> options) : DbContext(options)
{
    public DbSet<InstantEntity> Entities => Set<InstantEntity>();
}
