using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Jobs.EFCore.Entities;

/// <summary>
///     EF Core configuration for <see cref="RecurringJobDefinition"/>. Table: <c>__RecurringJobs</c>.
/// </summary>
public sealed class RecurringJobEntityTypeConfiguration : IEntityTypeConfiguration<RecurringJobDefinition>
{
    public const string TableName = "__RecurringJobs";

    public void Configure(EntityTypeBuilder<RecurringJobDefinition> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasMaxLength(256);
        builder.Property(e => e.JobType).HasMaxLength(512).IsRequired();
        builder.Property(e => e.CronExpression).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ParameterType).HasMaxLength(512);
        // Cap recurring-job JSON payload at 64KB (see JobEntityTypeConfiguration).
        builder.Property(e => e.ParametersJson).HasMaxLength(65535);
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.TimeZoneId).HasMaxLength(64);

        // Due jobs index
        builder.HasIndex(e => new { e.IsEnabled, e.NextExecutionAt });
    }
}
