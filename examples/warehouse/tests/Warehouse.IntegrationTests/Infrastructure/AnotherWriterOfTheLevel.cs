using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Warehouse.Stock.Entities;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     Another writer of one product's level, at the worst moment: between the read and the save of the
///     first write to it, once.
/// </summary>
/// <remarks>
///     <para>
///         The race a reservation runs in production — the other instance, or an expiry, saving the same
///         level first — held open on every run instead of hoped for. The update changes nothing but the
///         row's version (<c>xmin</c>, the token <c>[ConcurrencyAware]</c> maps on PostgreSQL), from a
///         connection of its own, committed before the save it interrupts goes on.
///     </para>
///     <para>
///         Once, so a writer that reads again finds the row settled. The product decides which save it
///         is: a host's other work on other levels passes through untouched.
///     </para>
/// </remarks>
internal sealed class AnotherWriterOfTheLevel(string connectionString, Guid productId) : SaveChangesInterceptor
{
    private int _pending = 1;

    /// <summary>Whether the other write has happened.</summary>
    public bool Wrote => Volatile.Read(ref _pending) == 0;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var level = eventData.Context?.ChangeTracker.Entries<StockLevel>()
            .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.ProductId == productId);

        if (level is not null && Interlocked.Exchange(ref _pending, 0) == 1)
        {
            var connection = new NpgsqlConnection(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = new NpgsqlCommand(
                    """UPDATE "StockLevels" SET "OnHand" = "OnHand" WHERE "PersistenceId" = @id""", connection);
                await using (command.ConfigureAwait(false))
                {
                    command.Parameters.AddWithValue("id", level.Entity.PersistenceId);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return result;
    }
}
