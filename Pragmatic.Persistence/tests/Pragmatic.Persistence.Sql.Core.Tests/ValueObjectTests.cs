using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.ValueObjects;

/// <summary>The repository's attribute, read by name as it reads <c>[Owned]</c>: a record of several members flattened into the owner's table.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ValueObjectAttribute : Attribute;

/// <summary>The repository's shape (<c>PostalAddress</c>, <c>ContactInfo</c>, <c>StorageSlot</c>): a constructor of the members, init properties, no parameterless constructor.</summary>
[ValueObject]
public sealed record ShipmentAddress
{
    public ShipmentAddress(string street, string city, int floor)
    {
        Street = street;
        City = city;
        Floor = floor;
    }

    public string Street { get; init; }

    public string City { get; init; }

    public int Floor { get; init; }
}

/// <summary>A single-value object (the repository's <c>Email</c>: a get-only Value, built by a factory), stored as its value through a converter.</summary>
[ValueObject]
public sealed record ShipmentCode
{
    private ShipmentCode(string value) => Value = value;

    public string Value { get; }

    public static ShipmentCode Create(string value) => new(value.ToUpperInvariant());
}

public sealed class ShipmentCodeConverter : ISqlConverter<ShipmentCode, string>
{
    public static string ToStore(ShipmentCode value) => value.Value;

    public static ShipmentCode FromStore(string value) => ShipmentCode.Create(value);
}

/// <summary>The Shipments table seen with value objects: the addresses as constructed records, the code as a converted single value.</summary>
[Entity]
[Table("Shipments")]
public sealed class Parcel
{
    public Guid Id { get; set; }

    [Convert<ShipmentCodeConverter>]
    public ShipmentCode Code { get; set; } = ShipmentCode.Create("");

    public ShipmentAddress Destination { get; set; } = new("", "", 0);

    public ShipmentAddress? Return { get; set; }
}

/// <summary>
///     The repository's value objects as the library maps them: a [ValueObject] record of several members is owned (its columns prefixed with the
///     property's name) and built through its constructor, an optional one null when its columns are; a single-value one is a converted column.
///     Written, read whole, compared by member and by value, projected. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class ValueObjectTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Value_objects_are_owned_records_and_converted_values(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "vo" + Guid.NewGuid().ToString("N")[..6];
        var home = new Parcel { Id = Guid.NewGuid(), Code = ShipmentCode.Create(mark + "-a"), Destination = new ShipmentAddress("Via Roma 1", "Torino", 3), Return = new ShipmentAddress("Corso Francia 2", "Torino", 0) };
        var away = new Parcel { Id = Guid.NewGuid(), Code = ShipmentCode.Create(mark + "-b"), Destination = new ShipmentAddress("Rue de Rivoli 5", "Paris", 1) };
        await session.InsertManyAsync(new[] { home, away });

        var read = await session.Query<Parcel>().Where((p, id) => p.Id == id, home.Id).FirstOrDefaultAsync();
        Assert.Equal(home.Destination, read!.Destination); // a record: equal by value
        Assert.Equal(home.Return, read.Return);
        Assert.Equal(home.Code, read.Code);
        var other = await session.Query<Parcel>().Where((p, id) => p.Id == id, away.Id).FirstOrDefaultAsync();
        Assert.Null(other!.Return);

        // A member in a predicate, the converted value compared as stored, a projection of the whole object.
        var inParis = await session.Query<Parcel>().Where((p, c) => p.Destination.City == c.City && p.Code == c.Code, (City: "Paris", Code: ShipmentCode.Create(mark + "-b"))).ToListAsync();
        Assert.Equal([away.Id], inParis.Select(p => p.Id));
        var projected = await session.Query<Parcel>().Where((p, m) => p.Destination.City == m, "Torino").Select(p => new { p.Code, p.Destination }).ToListAsync();
        Assert.Contains(projected, p => p.Code == home.Code && p.Destination == home.Destination);

        // An update replaces the record (immutable: a new one).
        home.Destination = home.Destination with { Floor = 4 };
        await session.UpdateAsync(home);
        Assert.Equal(4, (await session.Query<Parcel>().Where((p, id) => p.Id == id, home.Id).FirstOrDefaultAsync())!.Destination.Floor);
        transaction.Rollback();
    }
}
