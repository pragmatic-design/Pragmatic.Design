using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.Benchmarks.Workload;

public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>().HasKey(p => p.Id);
        modelBuilder.Entity<Project>().Property(p => p.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Ticket>().HasKey(t => t.Id);
        modelBuilder.Entity<Ticket>().Property(t => t.Amount).HasPrecision(18, 2);
    }
}
