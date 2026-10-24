using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     A hierarchy with a table per type (Vehicles, Cars, Trucks; read through VehiclesView): writes through the root or the type go to the root's
///     table and the type's own in one command, reads answer each row as its type with its own columns, an update moves the root's version and
///     writes both tables, an upsert writes the root's table then the type's (a new row inserted in both, a known one updated at its version),
///     a delete removes both rows. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class TptTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_table_per_type_hierarchy_reads_and_writes_both_tables(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "TPT-" + Guid.NewGuid().ToString("N")[..6];
        var car = new Car { Id = Guid.NewGuid(), Plate = mark + "-car", Seats = 5 };
        var truck = new Truck { Id = Guid.NewGuid(), Plate = mark + "-truck", Payload = 12.5m };
        var plain = new Vehicle { Id = Guid.NewGuid(), Plate = mark + "-plain" };
        await session.InsertAsync<Vehicle>(car); // through the root: the car's two tables
        await session.InsertAsync(truck); // through its own type
        Assert.Equal(1, await session.InsertManyAsync<Vehicle>([plain])); // rows, not statements

        static string Show(Vehicle v) => v switch { Car c => $"car:{c.Seats}", Truck t => $"truck:{t.Payload.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}", _ => "vehicle" };
        var all = await session.Query<Vehicle>().Where((v, m) => Sql.Like(v.Plate, m), mark + "%").OrderBy(v => v.Plate).ToListAsync();
        Assert.Equal(["car:5", "vehicle", "truck:12.5"], all.Select(Show));
        Assert.Equal([5], (await session.Query<Car>().Where((c, m) => Sql.Like(c.Plate, m), mark + "%").ToListAsync()).Select(c => c.Seats));
        Assert.Equal(1, await session.Query<Vehicle>().CountAsync((v, m) => Sql.Like(v.Plate, m) && v is Truck, mark + "%"));

        // An update through the root: the root's table (the version moves) and the car's own.
        car.Plate = mark + "-car2";
        car.Seats = 7;
        await session.UpdateAsync<Vehicle>(car);
        Assert.Equal(1, car.Version);
        var read = (await session.Query<Car>().FirstOrDefaultAsync((c, id) => c.Id == id, car.Id))!;
        Assert.Equal((mark + "-car2", 7, 1), (read.Plate, read.Seats, read.Version));
        var stale = new Car { Id = car.Id, Plate = "stale", Seats = 1, Version = 0 };
        await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.UpdateAsync(stale));

        // An upsert through the root: a new row inserted in both tables, a known one updated in both at its version (read back).
        var van = new Truck { Id = Guid.NewGuid(), Plate = mark + "-van", Payload = 3m };
        await session.UpsertAsync<Vehicle>(van);
        truck.Payload = 20m;
        await session.UpsertAsync<Vehicle>(truck);
        Assert.Equal(1, truck.Version);
        var trucks = await session.Query<Truck>().Where((t, m) => Sql.Like(t.Plate, m), mark + "%").OrderBy(t => t.Plate).ToListAsync();
        Assert.Equal([(mark + "-truck", 20m), (mark + "-van", 3m)], trucks.Select(t => (t.Plate, t.Payload)));
        var staleTruck = new Truck { Id = truck.Id, Plate = "stale", Payload = 99m, Version = 0 };
        await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.UpsertAsync<Vehicle>(staleTruck));
        Assert.Equal(20m, (await session.Query<Truck>().FirstOrDefaultAsync((t, id) => t.Id == id, truck.Id))!.Payload);

        await session.DeleteAsync<Vehicle>(truck);
        Assert.Equal(["car:7", "vehicle", "truck:3"], (await session.Query<Vehicle>().Where((v, m) => Sql.Like(v.Plate, m), mark + "%").OrderBy(v => v.Plate).ToListAsync()).Select(Show));
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_third_level_is_in_three_tables(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "TPT3-" + Guid.NewGuid().ToString("N")[..6];
        var sports = new SportsCar { Id = Guid.NewGuid(), Plate = mark + "-s", Seats = 2, TopSpeed = 300 };
        var car = new Car { Id = Guid.NewGuid(), Plate = mark + "-c", Seats = 5 };
        await session.InsertAsync<Vehicle>(sports); // Vehicles, Cars, SportsCars in one command
        await session.InsertAsync<Vehicle>(car);

        // Read as the root, as the middle type (its subtree), as the deepest type: each row its own type with every level's columns.
        static string Show(Vehicle v) => v switch { SportsCar s => $"sports:{s.Seats}:{s.TopSpeed}", Car c => $"car:{c.Seats}", _ => "vehicle" };
        Assert.Equal(["car:5", "sports:2:300"], (await session.Query<Vehicle>().Where((v, m) => Sql.Like(v.Plate, m), mark + "%").OrderBy(v => v.Plate).ToListAsync()).Select(Show));
        Assert.Equal(["car:5", "sports:2:300"], (await session.Query<Car>().Where((c, m) => Sql.Like(c.Plate, m), mark + "%").OrderBy(c => c.Plate).ToListAsync()).Select(c => Show(c)));
        Assert.Equal([300], (await session.Query<SportsCar>().Where((s, m) => Sql.Like(s.Plate, m), mark + "%").ToListAsync()).Select(s => s.TopSpeed));
        Assert.Equal(1, await session.Query<Vehicle>().CountAsync((v, m) => Sql.Like(v.Plate, m) && v is SportsCar, mark + "%"));

        // An update writes the three tables (the version in the root's); an upsert too; a delete removes the three rows.
        sports.Seats = 4;
        sports.TopSpeed = 320;
        await session.UpdateAsync<Vehicle>(sports);
        Assert.Equal(1, sports.Version);
        sports.TopSpeed = 330;
        await session.UpsertAsync<Vehicle>(sports);
        var read = (await session.Query<SportsCar>().FirstOrDefaultAsync((s, id) => s.Id == id, sports.Id))!;
        Assert.Equal((4, 330, 2), (read.Seats, read.TopSpeed, read.Version));
        await session.DeleteAsync<Vehicle>(sports);
        Assert.Equal(["car:5"], (await session.Query<Vehicle>().Where((v, m) => Sql.Like(v.Plate, m), mark + "%").ToListAsync()).Select(Show));
        Assert.Equal(0, await session.Query<SportsCar>().CountAsync((s, id) => s.Id == id, sports.Id));
        transaction.Rollback();
    }
}
