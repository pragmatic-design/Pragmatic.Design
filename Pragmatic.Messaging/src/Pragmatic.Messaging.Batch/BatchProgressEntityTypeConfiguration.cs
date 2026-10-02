using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     EF Core configuration for <see cref="BatchProgress"/> persistence.
///     Table: <c>__BatchProgress</c>.
/// </summary>
public sealed class BatchProgressEntityTypeConfiguration : IEntityTypeConfiguration<BatchProgress>
{
    public const string TableName = "__BatchProgress";

    public void Configure(EntityTypeBuilder<BatchProgress> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.Total).IsRequired();
        builder.Property(e => e.Completed).IsRequired();
        builder.Property(e => e.Failed).IsRequired();
        builder.Property(e => e.DispatchedCount).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(256);
        // NOT NULL (empty string in single-tenant hosts) — mirrors the tenant-isolated saga table.
        builder.Property(e => e.TenantId).HasMaxLength(128).IsRequired();

        // Ignore computed properties
        builder.Ignore(e => e.Pending);
        builder.Ignore(e => e.IsComplete);
        builder.Ignore(e => e.ProgressPercent);

        // Index on CompletedAt to support active-batch queries.
        // HasFilter is intentionally omitted: PostgreSQL double-quote syntax fails on SQL Server/SQLite.
        // A partial-index filter can be added per-provider in a derived configuration if needed.
        builder.HasIndex(e => e.CompletedAt);
    }
}
