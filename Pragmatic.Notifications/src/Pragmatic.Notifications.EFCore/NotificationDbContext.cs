using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Notifications.EFCore;

/// <summary>
///     DbContext for notification tracking. Uses __Notifications table.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    internal DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ApplyNotificationConfigurations(modelBuilder);

    /// <summary>
    ///     Applies the <c>__Notifications</c> mapping to the given model builder, for consumers keeping
    ///     this table alongside their own data.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>This is the shape that works in a composed host, and the only one.</b> A second
    ///         context against the same database creates none of its tables: <c>EnsureCreated</c> is
    ///         all-or-nothing per database, so it finds the database already there and stops — as
    ///         <c>PrivacyDbContext.ApplyPrivacyConfigurations</c> also says. Audit, Privacy, Cryptography
    ///         and Messaging each offer this method and the generated boundary context calls it. Without
    ///         this call, <c>UseEfCoreStore()</c> wires a store whose table does not exist and the first
    ///         send fails at run time.
    ///     </para>
    ///     <para>
    ///         A boundary cannot declare that it stores notifications, so an application calls this
    ///         from its own <c>OnModelCreating</c> — or, for a Pragmatic host whose contexts are
    ///         generated, creates the schema itself as the package's sample does.
    ///     </para>
    /// </remarks>
    public static void ApplyNotificationConfigurations(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new NotificationEntityTypeConfiguration());
}
