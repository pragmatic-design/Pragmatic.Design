using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Temporal;

/// <summary>
///     Dedicated DbContext for the temporal-relation demo. EF Core InMemory — the generated
///     <c>Active()</c>/<c>ActiveAt()</c>/<c>ForDepartment()</c> extensions are plain LINQ
///     predicates and translate fine in-memory.
/// </summary>
public sealed class TemporalDbContext(DbContextOptions<TemporalDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeAssignment> Assignments => Set<EmployeeAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Department>(e =>
        {
            e.HasKey(d => d.PersistenceId);
            e.Property(d => d.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<EmployeeAssignment>(e =>
        {
            e.HasKey(a => a.PersistenceId);
            e.Property(a => a.Role).HasMaxLength(100);
        });
    }
}
