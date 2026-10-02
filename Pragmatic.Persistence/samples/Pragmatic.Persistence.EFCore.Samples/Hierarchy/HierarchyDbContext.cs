using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Hierarchy;

/// <summary>
///     Dedicated DbContext for the hierarchy demo. Uses SQLite (a relational provider) because
///     the generated <c>GetDescendants</c>/<c>GetAncestors</c> run a recursive CTE — InMemory
///     cannot execute raw SQL.
/// </summary>
public sealed class HierarchyDbContext(DbContextOptions<HierarchyDbContext> options) : DbContext(options)
{
    public DbSet<Folder> Folders => Set<Folder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Folder>(e =>
        {
            e.HasKey(f => f.PersistenceId);
            e.Property(f => f.Name).HasMaxLength(100);
        });
    }
}
