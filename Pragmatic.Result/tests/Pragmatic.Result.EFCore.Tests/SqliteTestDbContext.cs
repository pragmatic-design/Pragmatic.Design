using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

// A SQLite-backed context (real relational provider) used for tests that need a genuine
// DbUpdateException or cancellation-token propagation, which the InMemory provider cannot produce.
public class SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : DbContext(options)
{
    public DbSet<SqliteUniqueEntity> Entities => Set<SqliteUniqueEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SqliteUniqueEntity>().HasIndex(e => e.Email).IsUnique();
}

public class SqliteUniqueEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
}
