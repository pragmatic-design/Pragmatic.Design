using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>The rows of a hand-written statement, as the row shape of the test: no table, no navigations.</summary>
[MapFrom<Order>]
public sealed class OrderTotalDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>
///     A hand-written statement as the root of a chain (<c>session.Sql&lt;T&gt;</c>): its holes are parameters, its identifiers follow
///     the dialect's quoting, the chain's operators apply on top, and it reads as the entity or as a [MapFrom] DTO.
/// </summary>
[Collection("databases")]
public class RawSqlTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_hand_written_statement_is_the_from_of_the_plan_and_its_holes_the_first_parameters()
    {
        var minimum = 1000m;
        var customerId = Guid.NewGuid();
        // The statement as written, the hole @p0, the chain's predicate after it (@p1), the paging last; SQL Server quoting reaches into the statement.
        var query = Session(SqlDialect.SqlServer).Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}").Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(5);
        Assert.Equal("SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted] FROM (SELECT * FROM [Orders] WHERE [Total] > @p0) t WHERE (t.[CustomerId] = @p1) ORDER BY t.[Number] OFFSET 0 ROWS FETCH NEXT @p2 ROWS ONLY", query.ToSql());
        using var command = query.ToCommand();
        Assert.Equal([minimum, customerId, 5], Enumerable.Range(0, command.Parameters.Count).Select(i => command.Parameters[i].Value).ToArray());

        // A DTO as the rows: its properties name the statement's columns.
        var dto = Session(SqlDialect.PostgreSql).Sql<OrderTotalDto>($"SELECT \"Id\", \"Number\", \"Total\" FROM \"Orders\" WHERE \"Total\" > {minimum}").OrderByDescending(o => o.Total).Take(3);
        Assert.Equal("SELECT t.\"Id\", t.\"Number\", t.\"Total\" FROM (SELECT \"Id\", \"Number\", \"Total\" FROM \"Orders\" WHERE \"Total\" > @p0) t ORDER BY t.\"Total\" DESC LIMIT @p1", dto.ToSql());

        // The runtime path: the same statement from the handler's text, the values first.
        var session = Session(SqlDialect.Sqlite);
        IQueryable<Order> composed = session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}");
        Assert.Equal(session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}").Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(5).ToSql(), composed.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number).Take(5).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_hand_written_statement_reads_the_seed_through_the_chain(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        var minimum = 1000m;

        var mine = await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}").Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId).OrderBy(o => o.Number).ToListAsync();
        var expected = seed.Orders.Where(o => o.Total > minimum && o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number, StringComparer.Ordinal).Select(o => o.Id);
        Assert.Equal(expected, mine.Select(o => o.Id));
        Assert.Equal(seed.Orders.Count(o => o.Total > minimum), await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}").CountAsync());

        // A DTO, and an include through the raw rows' foreign key.
        var top = await session.Sql<OrderTotalDto>($"SELECT \"Id\", \"Number\", \"Total\" FROM \"Orders\" WHERE \"CustomerId\" = {customerId}").OrderByDescending(o => o.Total).ThenBy(o => o.Id).Take(3).ToListAsync();
        Assert.Equal(seed.Orders.Where(o => o.CustomerId == customerId).OrderByDescending(o => o.Total).ThenBy(o => o.Id).Take(3).Select(o => (o.Id, o.Number, o.Total)), top.Select(t => (t.Id, t.Number, t.Total)));
        var withCustomer = await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"CustomerId\" = {customerId}").Include(o => o.Customer).OrderBy(o => o.Number).Take(2).ToListAsync();
        Assert.All(withCustomer, o => Assert.Equal(seed.BusyCustomer.Name, o.Customer!.Name));

        // The runtime path, entity rows.
        IQueryable<Order> composed = session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}");
        Assert.Equal(seed.Orders.Count(o => o.Total > minimum && o.CustomerId == customerId), await composed.Where(o => o.CustomerId == customerId).CountAsync());
    }
}
