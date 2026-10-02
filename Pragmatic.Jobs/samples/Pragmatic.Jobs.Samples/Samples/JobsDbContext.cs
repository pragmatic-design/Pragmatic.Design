using Microsoft.EntityFrameworkCore;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore.Entities;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Minimal application DbContext that hosts the Jobs persistence tables.
///     Applies the two SG-shipped entity configurations from
///     <c>Pragmatic.Jobs.EFCore</c> so the <c>__Jobs</c> / <c>__RecurringJobs</c>
///     tables map to <see cref="JobInstance"/> and <see cref="RecurringJobDefinition"/>.
///     In a real app this is your own DbContext — you just call
///     <c>ApplyConfiguration</c> for the two job configurations.
/// </summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
    }
}
