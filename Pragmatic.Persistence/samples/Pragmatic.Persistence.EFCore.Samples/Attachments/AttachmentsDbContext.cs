using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Attachments;

/// <summary>
///     Dedicated DbContext for the polymorphic-attachment demo. EF Core InMemory.
/// </summary>
public sealed class AttachmentsDbContext(DbContextOptions<AttachmentsDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<CommentNote> Comments => Set<CommentNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Ticket>(e => e.HasKey(t => t.PersistenceId));
        modelBuilder.Entity<Article>(e => e.HasKey(a => a.PersistenceId));
        modelBuilder.Entity<CommentNote>(e =>
        {
            e.HasKey(c => c.PersistenceId);
            e.Property(c => c.Text).HasMaxLength(500);
            // OwnerType / OwnerId are generated string columns — no extra mapping needed.
        });
    }
}
