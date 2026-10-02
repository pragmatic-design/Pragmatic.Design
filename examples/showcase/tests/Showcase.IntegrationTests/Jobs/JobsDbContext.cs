using Microsoft.EntityFrameworkCore;
using Pragmatic.Jobs.EFCore.Entities;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     Minimal DbContext carrying only the job tables, used to exercise <c>EfCoreJobStore</c>
///     against real PostgreSQL.
/// </summary>
/// <remarks>
///     The Showcase host runs jobs on the in-memory stores, so nothing else in the test suite
///     reaches the EF Core implementation — and its lease protocol is precisely the part that only
///     a real relational provider can validate (<c>ExecuteUpdateAsync</c>, concurrent UPDATE
///     predicates, timestamp comparison).
/// </remarks>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
    }
}
