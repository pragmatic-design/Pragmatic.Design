using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pragmatic.Notifications.EFCore;

/// <summary>
///     EF Core entity configuration for the __Notifications table.
/// </summary>
internal sealed class NotificationEntityTypeConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("__Notifications");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.RecipientAddress).HasMaxLength(512).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(1024).IsRequired();
        builder.Property(e => e.ProviderId).HasMaxLength(256);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2048);
        builder.Property(e => e.TenantId).HasMaxLength(128);
        builder.Property(e => e.Category).HasMaxLength(256);

        builder.Property(e => e.Audience).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(e => e.MetadataJson).HasColumnType("text");

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
    }
}
