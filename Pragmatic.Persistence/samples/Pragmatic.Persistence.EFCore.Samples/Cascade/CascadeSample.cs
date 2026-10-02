using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Events;

namespace Pragmatic.Persistence.EFCore.Samples.Cascade;

/// <summary>
///     Demonstrates <c>[CascadeOn&lt;RoomRate&gt;]</c> property propagation. In a real project the
///     attribute on <c>BookingCharge.UnitPrice</c> makes the SG emit
///     <c>BookingChargeUnitPriceCascadeHandler</c> — an
///     <c>IDomainEventHandler&lt;EntityPropertyChanged&lt;RoomRate&gt;&gt;</c> that, on a
///     <c>RoomRate.Rate</c> change, bulk-updates <c>BookingCharge.UnitPrice</c> for every related
///     charge via <c>ExecuteUpdateAsync</c>. This sample wires the exact equivalent handler by hand
///     and runs it against SQLite (ExecuteUpdate needs a relational provider).
/// </summary>
public static class CascadeSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Cascade ([CascadeOn<RoomRate>] → BookingCharge.UnitPrice) ═══");
        Console.WriteLine();

        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        try
        {
            var options = new DbContextOptionsBuilder<CascadeDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var db = new CascadeDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var deluxe = new RoomRate { RoomClass = "Deluxe", Rate = 120m };
            var standard = new RoomRate { RoomClass = "Standard", Rate = 99m };
            db.Rates.AddRange(deluxe, standard);

            // The declared relation is a real foreign key: the charge that must stay untouched
            // belongs to another rate, not to a rate that does not exist.
            var night1 = new BookingCharge { Label = "Night 1", UnitPrice = 120m };
            night1.SetRoomRateId(deluxe.Id);
            var night2 = new BookingCharge { Label = "Night 2", UnitPrice = 120m };
            night2.SetRoomRateId(deluxe.Id);
            var unrelated = new BookingCharge { Label = "Unrelated", UnitPrice = 99m };
            unrelated.SetRoomRateId(standard.Id);
            db.Charges.AddRange(night1, night2, unrelated);
            await db.SaveChangesAsync();

            Console.WriteLine("  Before rate change:");
            await Print(db);

            // The rate jumps to 150. In the host the entity setter raises EntityPropertyChanged and
            // the generated cascade handler reacts; here we raise the event and run the handler.
            deluxe.Rate = 150m;
            await db.SaveChangesAsync();

            var changeEvent = EntityPropertyChanged<RoomRate>.Create(
                entityId: deluxe.Id, propertyName: nameof(RoomRate.Rate), oldValue: 120m, newValue: 150m);

            await HandleCascadeAsync(db, changeEvent);

            Console.WriteLine("  After RoomRate.Rate 120 → 150 (cascade handler ran):");
            await Print(db);
            Console.WriteLine("  (Only charges with RoomRateId == Deluxe were updated; 'Unrelated' stays at 99.)");
            Console.WriteLine();
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>
    ///     Body-for-body equivalent of the SG-generated <c>BookingChargeUnitPriceCascadeHandler</c>:
    ///     on a <c>RoomRate.Rate</c> change, set <c>UnitPrice</c> for all charges of that rate.
    /// </summary>
    private static async Task HandleCascadeAsync(CascadeDbContext db, EntityPropertyChanged<RoomRate> @event)
    {
        if (@event.PropertyName != nameof(RoomRate.Rate)) return;

        var changedRateId = (Guid)@event.EntityId;
        var newPrice = (decimal)@event.NewValue!;

        await db.Charges
            .Where(c => c.RoomRateId == changedRateId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UnitPrice, newPrice));
    }

    private static async Task Print(CascadeDbContext db)
    {
        // Fresh read — ExecuteUpdate bypasses the change tracker.
        foreach (var c in await db.Charges.AsNoTracking().OrderBy(c => c.Label).ToListAsync())
            Console.WriteLine($"    {c.Label,-10} UnitPrice = {c.UnitPrice:C}");
        Console.WriteLine();
    }
}
