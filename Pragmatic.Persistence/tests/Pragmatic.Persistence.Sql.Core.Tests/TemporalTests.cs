using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Temporal reads: <c>AsOf(utcInstant)</c> answers the rows as they were: SQL Server's system-versioned table (<c>FOR SYSTEM_TIME AS OF</c>),
///     Oracle's Flashback, the history table the triggers of a [Temporal] table keep on PostgreSQL, SQLite and MySQL.
/// </summary>
[Collection("databases")]
public class TemporalTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task As_of_reads_the_row_as_it_was(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var price = new Price { Id = Guid.NewGuid(), Sku = "TEMPORAL-" + Guid.NewGuid().ToString("N")[..6], Amount = 10m };
        await session.InsertAsync(price);
        // The instant between the two versions must come after the insert's commit. SQL Server's period resolution is well under 100 ms.
        // Oracle's Flashback turns the instant into an SCN through its time-to-SCN mapping, recorded every few seconds and sparse just
        // after the server starts: the instant is the server's own, taken once the mapping places it at or after the insert's SCN.
        DateTime between;
        if (dialect == SqlDialect.Oracle)
            between = await OracleInstantAfterAsync(session, price.Id);
        else
        {
            await Task.Delay(100);
            between = DateTime.UtcNow;
            await Task.Delay(100);
        }
        price.Amount = 20m;
        await session.UpdateAsync(price);

        var current = await session.Query<Price>().FirstOrDefaultAsync((p, id) => p.Id == id, price.Id);
        Assert.Equal(20m, current!.Amount);
        var asOf = await session.Query<Price>().AsOf(between).Where((p, id) => p.Id == id, price.Id).ToListAsync();
        Assert.Single(asOf);
        // Every dialect answers the version valid at the instant.
        Assert.Equal(10m, asOf[0].Amount);
        if (dialect == SqlDialect.Oracle)
            Assert.Contains(" AS OF TIMESTAMP CAST(:p0 AS TIMESTAMP) t ", session.Query<Price>().AsOf(between).Where((p, id) => p.Id == id, price.Id).ToCommand().CommandText, StringComparison.Ordinal);
        if (dialect is SqlDialect.SqlServer or SqlDialect.PostgreSql or SqlDialect.Sqlite or SqlDialect.MySql)
        {
            // Before the row existed: nothing.
            Assert.Empty(await session.Query<Price>().AsOf(between.AddHours(-1)).Where((p, id) => p.Id == id, price.Id).ToListAsync());
        }

        if (dialect == SqlDialect.SqlServer)
            Assert.Contains("FOR SYSTEM_TIME AS OF @p0", session.Query<Price>().AsOf(between).Where((p, id) => p.Id == id, price.Id).ToSql(), StringComparison.Ordinal);
        await session.DeleteAsync(price);
        if (dialect is SqlDialect.PostgreSql or SqlDialect.Sqlite or SqlDialect.MySql)
        {
            // The history: the two versions, the second closed by the delete; after it, the row as of the instant is still the first version.
            Assert.Contains("\"PricesHistory\"", session.Query<Price>().AsOf(between).Where((p, id) => p.Id == id, price.Id).ToSql(), StringComparison.Ordinal);
            Assert.Equal([10m], (await session.Query<Price>().AsOf(between).Where((p, id) => p.Id == id, price.Id).ToListAsync()).Select(p => p.Amount));
            Assert.Empty(await session.Query<Price>().AsOf(DateTime.UtcNow.AddSeconds(5)).Where((p, id) => p.Id == id, price.Id).ToListAsync());
        }
    }

    /// <summary>
    ///     The server's current UTC instant, once Oracle maps it (TIMESTAMP_TO_SCN, as AS OF TIMESTAMP does) to an SCN at or after the row's
    ///     (ORA_ROWSCN: the commit of the insert, or later). A wait on that condition, bounded at 30 s.
    /// </summary>
    private static async Task<DateTime> OracleInstantAfterAsync(SqlSession session, Guid id)
    {
        using var row = session.Connection.CreateCommand();
        row.CommandText = "SELECT ORA_ROWSCN FROM \"Prices\" WHERE \"Id\" = :id";
        Add(row, "id", id.ToString("D"));
        var inserted = Convert.ToDecimal(await row.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            using var now = session.Connection.CreateCommand();
            now.CommandText = "SELECT CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP) FROM DUAL";
            var instant = DateTime.SpecifyKind(Convert.ToDateTime(await now.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            using var mapped = session.Connection.CreateCommand();
            mapped.CommandText = "SELECT TIMESTAMP_TO_SCN(:instant) FROM DUAL";
            Add(mapped, "instant", instant);
            if (Convert.ToDecimal(await mapped.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) >= inserted)
                return instant;
            Assert.True(DateTime.UtcNow < deadline, $"Oracle's time-to-SCN mapping did not reach the insert's SCN {inserted} within 30 s");
            await Task.Delay(500);
        }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
