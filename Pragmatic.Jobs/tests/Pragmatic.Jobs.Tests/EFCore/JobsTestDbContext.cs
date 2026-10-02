using Microsoft.EntityFrameworkCore;
using Pragmatic.Jobs.EFCore.Entities;

namespace Pragmatic.Jobs.Tests.EFCore;

/// <summary>
///     Test DbContext for Jobs EFCore store tests using SQLite in-memory.
/// </summary>
public sealed class JobsTestDbContext(DbContextOptions<JobsTestDbContext> options) : DbContext(options)
{
    public DbSet<JobInstance> Jobs => Set<JobInstance>();
    public DbSet<RecurringJobDefinition> RecurringJobs => Set<RecurringJobDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new JobEntityTypeConfiguration().Configure(modelBuilder.Entity<JobInstance>());
        new RecurringJobEntityTypeConfiguration().Configure(modelBuilder.Entity<RecurringJobDefinition>());
    }
}
