using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.ValueObjects;

/// <summary>
///     A value object an entity keeps behind a private setter is read back: the reader built it in the row's initializer, which a private setter
///     refuses (CS0272), so the entity did not compile at all. Required and optional, on every dialect, in a transaction rolled back.
/// </summary>
[Collection("databases")]
public class AValueObjectBehindAPrivateSetterIsReadTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_value_objects_are_set_through_their_accessors(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "pv" + Guid.NewGuid().ToString("N")[..6];
        var both = new GuardedParcel { Id = Guid.NewGuid(), Code = ShipmentCode.Create(mark + "-a") };
        both.Ship(new ShipmentAddress("Via Roma 1", "Torino", 3), new ShipmentAddress("Corso Francia 2", "Torino", 0));
        var oneWay = new GuardedParcel { Id = Guid.NewGuid(), Code = ShipmentCode.Create(mark + "-b") };
        oneWay.Ship(new ShipmentAddress("Rue de Rivoli 5", "Paris", 1), null);
        await session.InsertManyAsync(new[] { both, oneWay });

        var read = await session.Query<GuardedParcel>().Where((p, id) => p.Id == id, both.Id).FirstOrDefaultAsync();
        Assert.Equal(both.Destination, read!.Destination);
        Assert.Equal(both.Return, read.Return);

        var other = await session.Query<GuardedParcel>().Where((p, id) => p.Id == id, oneWay.Id).FirstOrDefaultAsync();
        Assert.Equal(oneWay.Destination, other!.Destination);
        Assert.Null(other.Return);
        transaction.Rollback();
    }
}
