using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Converters;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     In-memory DbContext for integration tests.
///     Configures entity types, soft-delete query filters, and value converters.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options, TimeProvider? timeProvider = null)
    : DbContext(options)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DbSet<TestProduct> Products { get; set; } = null!;
    public DbSet<TestOrder> Orders { get; set; } = null!;
    public DbSet<TestCustomer> Customers { get; set; } = null!;
    public DbSet<TestDocument> Documents { get; set; } = null!;
    public DbSet<TestInvoice> Invoices { get; set; } = null!;
    public DbSet<TestArticle> Articles { get; set; } = null!;
    public DbSet<TestCategory> Categories { get; set; } = null!;
    public DbSet<TestAnnouncement> Announcements { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TestCategory - for Include testing
        modelBuilder.Entity<TestCategory>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        // TestProduct - simple entity with optional category navigation
        modelBuilder.Entity<TestProduct>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            b.Property(e => e.Price).HasPrecision(18, 2);
            b.HasOne(e => e.Category).WithMany(c => c.Products).HasForeignKey(e => e.CategoryId);
        });

        // TestOrder - soft-delete entity with query filter
        modelBuilder.Entity<TestOrder>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            b.Property(e => e.Total).HasPrecision(18, 2);
            b.HasQueryFilter(e => !e.IsDeleted);
        });

        // TestCustomer - auditable entity
        modelBuilder.Entity<TestCustomer>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.FullName).IsRequired().HasMaxLength(200);
            b.Property(e => e.Email).IsRequired().HasMaxLength(200);
        });

        // TestDocument - ShortGuid converter
        modelBuilder.Entity<TestDocument>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Title).IsRequired().HasMaxLength(200);
            b.Property(e => e.ExternalId).HasConversion<ShortGuidConverter>();
        });

        // TestInvoice - OpaqueId converter
        modelBuilder.Entity<TestInvoice>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.InvoiceNumber).IsRequired().HasMaxLength(50);
            b.Property(e => e.SequenceNumber).HasConversion(new OpaqueIdConverter("test-salt"));
        });

        // TestAnnouncement - raises domain events; the events themselves are not persisted
        modelBuilder.Entity<TestAnnouncement>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Headline).IsRequired().HasMaxLength(200);
            b.Ignore(e => e.DomainEvents);
        });

        // TestArticle - concurrency-aware entity with row version
        modelBuilder.Entity<TestArticle>(b =>
        {
            b.HasKey(e => e.PersistenceId);
            b.Property(e => e.Title).IsRequired().HasMaxLength(200);
            b.Property(e => e.Content).IsRequired();
            b.Property(e => e.RowVersion).IsConcurrencyToken();
        });
    }

    /// <summary>
    ///     Override SaveChanges to auto-populate audit fields for IAuditable entities.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    ///     Override SaveChangesAsync to auto-populate audit fields for IAuditable entities.
    /// </summary>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditFields()
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
