using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Jobs.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="JobInstance"/>. Table: <c>__Jobs</c>.
/// </summary>
public sealed class JobEntityTypeConfiguration : IEntityTypeConfiguration<JobInstance>
{
    public const string TableName = "__Jobs";

    public void Configure(EntityTypeBuilder<JobInstance> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.JobType).HasMaxLength(512).IsRequired();
        builder.Property(e => e.ParameterType).HasMaxLength(512);
        // Cap JSON payload at 64KB to prevent pathological-size payloads from
        // accidentally being persisted. Providers that support native JSON
        // (PostgreSQL jsonb, SQL Server json) can override at runtime.
        builder.Property(e => e.ParametersJson).HasMaxLength(65535);
        builder.Property(e => e.Error).HasMaxLength(4096);
        builder.Property(e => e.LeasedBy).HasMaxLength(128);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.ContinuationJobType).HasMaxLength(512);

        // Polling: filter on Status, then order by priority then schedule. Leads with the columns
        // the poll predicate and ORDER BY use, so it serves the query without a full scan.
        builder.HasIndex(e => new { e.Status, e.Priority, e.ScheduledFor });
        // Lease cleanup
        builder.HasIndex(e => new { e.Status, e.LeaseExpiresAt });
        // Correlation lookup
        builder.HasIndex(e => e.CorrelationId);
        // Retention purge: both existing indexes lead with Status, so a cutoff scan on
        // CompletedAt alone would be a full table scan on a large history table.
        builder.HasIndex(e => e.CompletedAt);
    }
}
