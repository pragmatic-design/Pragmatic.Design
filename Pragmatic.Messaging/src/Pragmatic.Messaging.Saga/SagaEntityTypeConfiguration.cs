using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     EF Core configuration for SagaInstance + SagaStep tables.
/// </summary>
public sealed class SagaEntityTypeConfiguration :
    IEntityTypeConfiguration<SagaInstance>,
    IEntityTypeConfiguration<SagaStep>
{
    public const string SagaInstanceTable = "__SagaInstances";
    public const string SagaStepTable = "__SagaSteps";

    public void Configure(EntityTypeBuilder<SagaInstance> builder)
    {
        builder.ToTable(SagaInstanceTable);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SagaType).HasMaxLength(512).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(SagaInstance.MaxCorrelationIdLength).IsRequired();
        builder.Property(e => e.State).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2048);
        builder.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
        // Manually-bumped optimistic concurrency token (portable across SQL Server/PostgreSQL/SQLite).
        builder.Property(e => e.Version).IsConcurrencyToken();

        // Filtered UNIQUE index: enforces at most one ACTIVE saga per (type, correlation, tenant).
        // TenantId is part of the key so two tenants can run the same (SagaType, CorrelationId)
        // concurrently without colliding (empty string in single-tenant hosts).
        // Completed/Compensated/TimedOut/Faulted sagas can stay in history without blocking
        // a future re-instance with the same correlation id (e.g. re-orderable workflows).
        // Status = 0 corresponds to SagaStatus.Active.
        builder.HasIndex(e => new { e.SagaType, e.CorrelationId, e.TenantId })
            .IsUnique()
            .HasFilter("\"Status\" = 0");
        builder.HasIndex(e => new { e.Status, e.SagaType });
        // Supports the saga-timeout background poll: only active sagas with a set deadline.
        builder.HasIndex(e => new { e.Status, e.TimeoutAt });

        builder.HasMany(e => e.Steps)
            .WithOne(s => s.SagaInstance)
            .HasForeignKey(s => s.SagaInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<SagaStep> builder)
    {
        builder.ToTable(SagaStepTable);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StepName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.Error).HasMaxLength(2048);
    }
}
