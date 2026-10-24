using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The model features beyond the workload's: every column type, converted columns (a stamp as ticks, an enum as
///     its name) in readers, writers, predicates and projections, on the compiled and the runtime path. The oracle is
///     the value written, read back through the session and, where the storage matters, through a raw reader.
/// </summary>
[Collection("databases")]
public class ModelTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Every_column_type_and_the_converted_columns_round_trip_and_filter(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var stamp = new DateTimeOffset(2026, 10, 19, 21, 30, 15, TimeSpan.FromHours(2));
        var row = new TypedRow
        {
            Id = Guid.NewGuid(),
            When = new DateTime(2026, 10, 19, 21, 30, 15, 123, DateTimeKind.Unspecified).AddTicks(4560), // microsecond precision: PostgreSQL's timestamp keeps six decimals, not seven
            Blob = [1, 2, 3, 250, 255],
            Day = new DateOnly(2026, 10, 19),
            Clock = new TimeOnly(21, 30, 15, 123),
            Span = TimeSpan.FromDays(40) + TimeSpan.FromMilliseconds(1234),
            Stamp = stamp,
            Status = OrderStatus.Paid,
            MaybeWhen = null,
            MaybeStamp = null,
        };
        await session.InsertAsync(row);
        var other = new TypedRow { Id = Guid.NewGuid(), When = row.When.AddDays(1), Blob = [], Day = row.Day.AddDays(1), Clock = TimeOnly.MinValue, Span = TimeSpan.Zero, Stamp = stamp.AddHours(3), Status = OrderStatus.Open, MaybeWhen = row.When, MaybeStamp = stamp };
        await session.InsertAsync(other);

        var read = await session.Query<TypedRow>().FirstOrDefaultAsync((r, id) => r.Id == id, row.Id);
        Assert.NotNull(read);
        Assert.Equal(row.When, read.When);
        Assert.Equal(row.Blob, read.Blob);
        Assert.Equal(row.Day, read.Day);
        Assert.Equal(row.Clock, read.Clock);
        Assert.Equal(row.Span, read.Span);
        Assert.Equal(stamp, read.Stamp); // the same instant, back from UTC ticks
        Assert.Equal(OrderStatus.Paid, read.Status);
        Assert.Null(read.MaybeWhen);
        Assert.Null(read.MaybeStamp);
        var second = await session.Query<TypedRow>().FirstOrDefaultAsync((r, id) => r.Id == id, other.Id);
        Assert.Equal(row.When, second!.MaybeWhen);
        Assert.Equal(stamp, second.MaybeStamp);

        // Predicates on converted columns: the value goes through the converter (an argument, a constant), on both paths.
        var since = stamp.AddHours(1);
        Assert.Equal(1, await session.Query<TypedRow>().CountAsync((r, since) => r.Stamp > since, since));
        Assert.Equal(1, await session.Query<TypedRow>().CountAsync(r => r.Status == OrderStatus.Paid));
        Assert.Equal(1, await session.Query<TypedRow>().CountAsync((r, maybe) => r.MaybeStamp == maybe, (DateTimeOffset?)stamp));
        IQueryable<TypedRow> runtime = session.Query<TypedRow>();
        Assert.Equal(1, await runtime.Where(r => r.Stamp > since).CountAsync());
        Assert.Equal(1, await runtime.Where(r => r.Status == OrderStatus.Paid).CountAsync());
        Assert.Equal(session.Query<TypedRow>().Where((r, since) => r.Stamp > since, since).ToSql(), runtime.Where(r => r.Stamp > since).ToSql());

        // Projections read the converted columns with the columns' readers.
        var dto = await session.Query<TypedRow>().Where((r, id) => r.Id == id, row.Id).Project<TypedStampDto>().FirstOrDefaultAsync();
        Assert.Equal((stamp, OrderStatus.Paid), (dto!.Stamp, dto.Status));
        var picked = await session.Query<TypedRow>().Where((r, id) => r.Id == id, row.Id).Select(r => new { r.Stamp, r.Status }).FirstOrDefaultAsync();
        Assert.Equal((stamp, OrderStatus.Paid), (picked!.Stamp, picked.Status));

        // What the table holds: ticks and a name, not a date and a number.
        using var command = session.Connection.CreateCommand();
        command.CommandText = $"SELECT {Workload.Schema.Quote(dialect, "Stamp")}, {Workload.Schema.Quote(dialect, "Status")} FROM {Workload.Schema.Quote(dialect, "TypedRows")} WHERE {Workload.Schema.Quote(dialect, "Id")} = @p0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@p0";
        parameter.Value = SqlValue.ToDatabase(dialect, row.Id);
        command.Parameters.Add(parameter);
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command); // :p0, as the library's own commands
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(stamp.UtcTicks, reader.GetInt64(0));
        Assert.Equal("Paid", reader.GetString(1));
        reader.Close();

        // Updates write through the converter too.
        row.Status = OrderStatus.Cancelled;
        row.Stamp = stamp.AddDays(1);
        await session.UpdateAsync(row);
        var updated = await session.Query<TypedRow>().FirstOrDefaultAsync((r, id) => r.Id == id, row.Id);
        Assert.Equal((OrderStatus.Cancelled, stamp.AddDays(1)), (updated!.Status, updated.Stamp));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task An_owned_type_is_flattened_into_the_owners_table_and_read_as_a_member_path(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var rome = new Shipment { Id = Guid.NewGuid(), Code = "S-1", Destination = new Address { Street = "Via Roma 1", City = "Rome", Floor = 3 }, Return = null };
        var milan = new Shipment { Id = Guid.NewGuid(), Code = "S-2", Destination = new Address { Street = "Corso Como 10", City = "Milan", Floor = 0 }, Return = new Address { Street = "Via Torino 5", City = "Turin", Floor = 1 } };
        await session.InsertManyAsync([rome, milan]);

        var read = await session.Query<Shipment>().FirstOrDefaultAsync((s, id) => s.Id == id, rome.Id);
        Assert.Equal(("Via Roma 1", "Rome", 3), (read!.Destination.Street, read.Destination.City, read.Destination.Floor));
        Assert.Null(read.Return);
        var second = await session.Query<Shipment>().FirstOrDefaultAsync((s, id) => s.Id == id, milan.Id);
        Assert.Equal(("Via Torino 5", "Turin", 1), (second!.Return!.Street, second.Return.City, second.Return.Floor));

        // Members in predicates and projections, compiled and at runtime: columns of this table, no join.
        var inRome = session.Query<Shipment>().Where(s => s.Destination.City == "Rome");
        Assert.Equal(1, await inRome.CountAsync());
        Assert.Equal("SELECT t.\"Id\", t.\"Code\", t.\"Destination_Street\", t.\"Destination_City\", t.\"Destination_Floor\", t.\"Return_Street\", t.\"Return_City\", t.\"Return_Floor\" FROM \"Shipments\" t WHERE (t.\"Destination_City\" = " + (dialect == SqlDialect.Oracle ? ":p0" : "@p0") + ")", session.Query<Shipment>().Where(s => s.Destination.City == "Rome").ToSql().Replace("[", "\"").Replace("]", "\""));
        IQueryable<Shipment> runtime = session.Query<Shipment>();
        Assert.Equal(inRome.ToSql(), runtime.Where(s => s.Destination.City == "Rome").ToSql());
        Assert.Equal(1, await runtime.Where(s => s.Destination.City == "Rome").CountAsync());
        var cities = await session.Query<Shipment>().OrderBy(s => s.Code).Select(s => new { s.Code, s.Destination.City, ReturnCity = s.Return!.City }).ToListAsync();
        Assert.Equal([("S-1", "Rome", null), ("S-2", "Milan", "Turin")], cities.Select(c => (c.Code, c.City, c.ReturnCity)).ToList());
        var dto = await session.Query<Shipment>().Where((s, id) => s.Id == id, rome.Id).Project<ShipmentRowDto>().FirstOrDefaultAsync();
        Assert.Equal(("Via Roma 1", "Rome"), (dto!.Street, dto.DestinationCity));

        // Writes go through the members; an optional owned type set later fills its columns, cleared again when set to null.
        rome.Return = new Address { Street = "Back 2", City = "Naples", Floor = 2 };
        await session.UpdateAsync(rome);
        Assert.Equal("Naples", (await session.Query<Shipment>().FirstOrDefaultAsync((s, id) => s.Id == id, rome.Id))!.Return!.City);
        rome.Return = null;
        await session.UpdateAsync(rome);
        Assert.Null((await session.Query<Shipment>().FirstOrDefaultAsync((s, id) => s.Id == id, rome.Id))!.Return);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_hierarchy_in_one_table_reads_each_row_as_its_type_and_filters_a_derived_type_by_discriminator(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var card = new CardCharge { Id = Guid.NewGuid(), Amount = 10m, Description = "card", Last4 = "1234" };
        var wire = new WireCharge { Id = Guid.NewGuid(), Amount = 20m, Description = "wire", Iban = "IT00X", Days = 2 };
        var plain = new Charge { Id = Guid.NewGuid(), Amount = 30m, Description = "plain" };
        await session.InsertAsync<Charge>(card); // the root's writer: the discriminator from the row's type, the other types' columns null
        await session.InsertAsync(wire); // the derived type's writer: its own discriminator
        await session.InsertAsync(plain);

        // The root reads every row as its own type; a derived type reads its rows only.
        var all = await session.Query<Charge>().OrderBy(c => c.Amount).ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal("1234", Assert.IsType<CardCharge>(all[0]).Last4);
        Assert.Equal(("IT00X", 2), Assert.IsType<WireCharge>(all[1]).Let(w => (w.Iban, w.Days)));
        Assert.IsType<Charge>(all[2]);
        var cards = await session.Query<CardCharge>().ToListAsync();
        Assert.Equal([card.Id], cards.Select(c => c.Id));
        Assert.Equal(1, await session.Query<WireCharge>().CountAsync((w, days) => w.Days >= days, 1));
        Assert.Equal(0, await session.Query<WireCharge>().CountAsync(w => w.Amount == 10m)); // the card's amount: not a wire
        Assert.Contains("(t.\"Kind\" = 'WireCharge')", session.Query<WireCharge>().Where(w => w.Days > 0).ToSql().Replace("[", "\"").Replace("]", "\""), StringComparison.Ordinal);

        // The runtime path carries the same predicate and the same readers.
        IQueryable<Charge> runtimeAll = session.Query<Charge>();
        Assert.Equal(3, (await runtimeAll.ToListAsync()).Count(c => c is CardCharge or WireCharge or Charge));
        IQueryable<CardCharge> runtimeCards = session.Query<CardCharge>();
        Assert.Equal(session.Query<CardCharge>().Where(c => c.Last4 == "1234").ToSql(), runtimeCards.Where(c => c.Last4 == "1234").ToSql());
        Assert.Single(await runtimeCards.Where(c => c.Last4 == "1234").ToListAsync());

        // Updates through the root and through the derived type; the discriminator stays what the row is.
        card.Last4 = "9999";
        await session.UpdateAsync<Charge>(card);
        wire.Days = 5;
        await session.UpdateAsync(wire);
        Assert.Equal("9999", (await session.Query<CardCharge>().FirstOrDefaultAsync((c, id) => c.Id == id, card.Id))!.Last4);
        Assert.Equal(5, (await session.Query<WireCharge>().FirstOrDefaultAsync((w, id) => w.Id == id, wire.Id))!.Days);
        Assert.Equal(3, await session.Query<Charge>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_hierarchy_walks_descendants_and_ancestors_with_one_recursive_cte(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var root = new Category { Id = Guid.NewGuid(), Name = "root" };
        var a = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = "a" };
        var a1 = new Category { Id = Guid.NewGuid(), ParentId = a.Id, Name = "a1" };
        var a2 = new Category { Id = Guid.NewGuid(), ParentId = a.Id, Name = "a2" };
        var b = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = "b" };
        var other = new Category { Id = Guid.NewGuid(), Name = "other root" };
        await session.InsertManyAsync([root, a, a1, a2, b, other]);

        var descendants = await session.GetDescendantsByParent(root.Id);
        Assert.Equal(["a", "a1", "a2", "b"], descendants.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(["a1", "a2"], (await session.GetDescendantsByParent(a.Id)).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(await session.GetDescendantsByParent(a1.Id));
        var ancestors = await session.GetAncestorsByParent(a1.Id);
        Assert.Equal(["a", "root"], ancestors.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(await session.GetAncestorsByParent(other.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_json_column_round_trips_through_the_source_generated_context(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var document = new Document { Id = Guid.NewGuid(), Name = "manual", Spec = new DocumentSpec { Title = "Manual", Tags = ["pdf", "v2"], Pages = 42 } };
        await session.InsertAsync(document);
        var read = await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, document.Id);
        Assert.Equal(("Manual", 42), (read!.Spec.Title, read.Spec.Pages));
        Assert.Equal(["pdf", "v2"], read.Spec.Tags);

        document.Spec.Pages = 43;
        await session.UpdateAsync(document);
        Assert.Equal(43, (await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, document.Id))!.Spec.Pages);

        // What the column holds: the JSON text (jsonb on PostgreSQL, through the cast the writer adds).
        using var command = session.Connection.CreateCommand();
        command.CommandText = $"SELECT {Workload.Schema.Quote(dialect, "Spec")} FROM {Workload.Schema.Quote(dialect, "Documents")} WHERE {Workload.Schema.Quote(dialect, "Id")} = @p0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@p0";
        parameter.Value = SqlValue.ToDatabase(dialect, document.Id);
        command.Parameters.Add(parameter);
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command); // :p0, as the library's own commands
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("\"Pages\"", stored, StringComparison.Ordinal);
        Assert.Contains("43", stored, StringComparison.Ordinal);
    }
}
