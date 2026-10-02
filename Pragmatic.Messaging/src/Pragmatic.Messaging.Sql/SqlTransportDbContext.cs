using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Messaging.Sql.Entities;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     The SQL transport's own DbContext (queue, subscription registry, dead letters). The EF
///     provider comes from <see cref="SqlTransportOptions.ConfigureDbContext"/> — the transport
///     can live on a dedicated database and does not pollute the app model.
/// </summary>
public sealed class SqlTransportDbContext(DbContextOptions<SqlTransportDbContext> options) : DbContext(options)
{
    public DbSet<TransportMessage> Messages => Set<TransportMessage>();
    public DbSet<TransportSubscription> Subscriptions => Set<TransportSubscription>();
    public DbSet<TransportDeadLetter> DeadLetters => Set<TransportDeadLetter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ApplyTransportConfigurations(modelBuilder);

        // Sqlite cannot compare/order DateTimeOffset columns (the claim predicate needs both).
        // Unit tests run on Sqlite, so map DateTimeOffset to a sortable binary long THERE ONLY —
        // PostgreSQL/SQL Server keep their native timestamp types (ops queries stay readable).
        // Safe because the transport always writes UTC (binary encoding sorts correctly at a
        // fixed offset).
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var converter = new DateTimeOffsetToBinaryConverter();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                        property.SetValueConverter(converter);
                }
            }
        }
    }

    /// <summary>
    ///     Escape hatch: co-locate the transport tables in the APP's DbContext (managed with
    ///     Pragmatic.Migrations) instead of the transport's own schema provisioning — call this
    ///     from the app's OnModelCreating and set <see cref="SqlTransportOptions.AutoCreateSchema"/>
    ///     to false.
    /// </summary>
    public static void ApplyTransportConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransportMessageEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new TransportSubscriptionEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new TransportDeadLetterEntityTypeConfiguration());
    }
}
