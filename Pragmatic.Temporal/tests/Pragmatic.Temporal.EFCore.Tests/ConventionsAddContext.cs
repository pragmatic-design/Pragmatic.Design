using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore.Conventions;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Context registering TemporalModelConvention manually in ConfigureConventions.</summary>
public class ConventionsAddContext(DbContextOptions<ConventionsAddContext> options) : DbContext(options)
{
    public DbSet<TemporalEntity> Entities => Set<TemporalEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TemporalModelConvention());
    }
}
