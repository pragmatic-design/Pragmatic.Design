using System.Data.Common;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Multi-tenancy by schema: a session with <see cref="SqlSession.Schema" /> runs every statement on that schema's tables
///     (the text qualified once per plan and schema, cached: no second translation), reads and writes alike; a pool routes a
///     tenant to its connection and schema. PostgreSQL and SQL Server schemas; on SQLite an attached database stands for the schema.
/// </summary>
[Collection("databases")]
public class TenantTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Two_tenants_run_the_same_plan_each_on_its_own_schema(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var a = await TenantSessionAsync(dialect, "tenant_a");
        using var b = await TenantSessionAsync(dialect, "tenant_b");

        // The writers' statements on the tenant's tables: two orders in a, one in b.
        var customerA = NewCustomer("A");
        var customerB = NewCustomer("B");
        await a.InsertAsync(customerA);
        await a.InsertManyAsync([NewOrder(customerA, "A-1", 10m), NewOrder(customerA, "A-2", 20m)]);
        await b.InsertAsync(customerB);
        await b.InsertAsync(NewOrder(customerB, "B-1", 30m));

        // One call site, one plan, two schemas: each session reads its own rows; the include's join is on the tenant's Customers too.
        var ofA = await Open(a);
        var ofB = await Open(b);
        Assert.Equal(["A-1", "A-2"], ofA.Select(o => o.Number));
        Assert.Equal(["B-1"], ofB.Select(o => o.Number));
        Assert.All(ofA, o => Assert.Equal("A", o.Customer!.Name));
        Assert.Equal(2, await Count(a));
        Assert.Equal(1, await Count(b));

        // The text names the schema, and is the same string instance on the second call: qualified once per plan and schema.
        var quotedA = dialect == SqlDialect.SqlServer ? "[tenant_a].[Orders]" : "\"tenant_a\".\"Orders\"";
        var quotedB = dialect == SqlDialect.SqlServer ? "[tenant_b].[Orders]" : "\"tenant_b\".\"Orders\"";
        Assert.Contains(quotedA, Text(a), StringComparison.Ordinal);
        Assert.Contains(quotedB, Text(b), StringComparison.Ordinal);
        Assert.Contains(dialect == SqlDialect.SqlServer ? "JOIN [tenant_a].[Customers]" : "JOIN \"tenant_a\".\"Customers\"", Text(a), StringComparison.Ordinal);
        Assert.Same(Text(a), Text(a));
        using (var plain = databases.Session(dialect))
            Assert.DoesNotContain("tenant_", plain.Query<Order>().Where(o => !o.IsDeleted).ToSql(), StringComparison.Ordinal); // a session without a schema: the text as generated

        // A command through the chain and an update: on the tenant's rows only.
        var orderA1 = ofA[0];
        orderA1.Total = 11m;
        await a.UpdateAsync(orderA1);
        Assert.Equal(11m, (await Open(a))[0].Total);
        Assert.Equal(1, await a.Query<Order>().Where((o, n) => o.Number == n, "A-2").ExecuteDeleteAsync());
        Assert.Equal(1, await Count(a));
        Assert.Equal(1, await Count(b));

        // The schema check reads the tenant's schema: its Orders table is there and matches.
        var report = await a.VerifySchemaAsync();
        Assert.DoesNotContain(report.Problems, p => p.Table == "Orders");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_pool_routes_a_tenant_to_its_connection_and_schema(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        // Two tenants on the same database, one schema each: the pool opens a session per tenant, sets its schema, and keeps idle sessions per tenant.
        await using var pool = new SqlSessionPool(tenant => databases.Connect(dialect), tenant => "tenant_" + tenant, dialect, capacity: 4, prepare: (session, tenant, ct) => PrepareTenantAsync(session, "tenant_" + tenant, ct));
        SqlSession first;
        using (var lease = await pool.RentAsync("a"))
        {
            first = lease.Session;
            Assert.Equal("tenant_a", first.Schema);
            Assert.Equal("a", first.Tenant);
            await first.InsertAsync(NewCustomer("pooled"));
        }

        using (var leaseB = await pool.RentAsync("b"))
        {
            Assert.Equal("tenant_b", leaseB.Session.Schema);
            Assert.NotSame(first, leaseB.Session);
            using var leaseA = await pool.RentAsync("a");
            Assert.Same(first, leaseA.Session); // the idle session of tenant a, with its prepared commands
            Assert.Equal(1, await leaseA.Session.Query<Customer>().CountAsync((c, n) => c.Name == n, "pooled"));
            Assert.Equal(0, await leaseB.Session.Query<Customer>().CountAsync((c, n) => c.Name == n, "pooled"));
        }

        Assert.Equal(2, pool.Idle);
    }

    // The same call sites for both tenants: one plan each.
    private static ValueTask<List<Order>> Open(SqlSession session) => session.Query<Order>().Include(o => o.Customer).Where(o => !o.IsDeleted).OrderBy(o => o.Number).ToListAsync();

    private static ValueTask<int> Count(SqlSession session) => session.Query<Order>().CountAsync();

    private static string Text(SqlSession session) => session.Query<Order>().Include(o => o.Customer).Where(o => !o.IsDeleted).OrderBy(o => o.Number).ToSql();

    private static Customer NewCustomer(string name) => new() { Id = Guid.NewGuid(), Name = name, Email = name.ToLowerInvariant() + "@tenant.test", Country = "IT", CreatedAt = DateTimeOffset.UtcNow };

    private static Order NewOrder(Customer customer, string number, decimal total) => new() { Id = Guid.NewGuid(), CustomerId = customer.Id, Number = number, Status = OrderStatus.Open, Total = total, CreatedAt = DateTimeOffset.UtcNow };

    /// <summary>A session on the tenant's schema, the schema created empty with the workload's tables.</summary>
    private async Task<SqlSession> TenantSessionAsync(SqlDialect dialect, string schema)
    {
        var session = new SqlSession(databases.Open(dialect), dialect) { Schema = schema };
        try
        {
            await PrepareTenantAsync(session, schema, default);
        }
        catch
        {
            session.Dispose(); // or the SQLite file stays open past the fixture's cleanup
            throw;
        }

        return session;
    }

    /// <summary>
    ///     The tenant's schema on the session's connection: created when missing (an attached in-memory database on SQLite, a schema on the
    ///     others), its tables dropped and created again from the workload's DDL, qualified as the session qualifies its statements.
    /// </summary>
    private static async Task PrepareTenantAsync(SqlSession session, string schema, CancellationToken ct)
    {
        var dialect = session.Dialect;
        if (dialect == SqlDialect.MySql)
            await SqlMySql.PrepareAsync(session.Connection, ct); // a connection the pool opened itself: ANSI_QUOTES and UTC first, as an application's prepare hook would
        if (dialect == SqlDialect.Oracle)
            await SqlOracle.PrepareAsync(session.Connection, ct);
        switch (dialect)
        {
            case SqlDialect.Oracle:
                await RunAsync(session.Connection, $"CREATE USER IF NOT EXISTS \"{schema}\" IDENTIFIED BY \"spike\" QUOTA UNLIMITED ON USERS", ct); // a schema is a user on Oracle
                break;
            case SqlDialect.Sqlite:
                // Microsoft.Data.Sqlite pools the physical connection, attachments included: a connection that already has the schema keeps it (its tables are recreated below).
                if (!await AttachedAsync(session.Connection, schema, ct))
                    await RunAsync(session.Connection, $"ATTACH DATABASE ':memory:' AS \"{schema}\"", ct);
                break;
            case SqlDialect.PostgreSql:
                await RunAsync(session.Connection, $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", ct);
                break;
            case SqlDialect.MySql:
                await RunAsync(session.Connection, $"CREATE DATABASE IF NOT EXISTS \"{schema}\"", ct); // a schema is a database on MySQL
                break;
            default:
                await RunAsync(session.Connection, $"IF SCHEMA_ID('{schema}') IS NULL EXEC('CREATE SCHEMA [{schema}]')", ct);
                break;
        }

        foreach (var table in Schema.Tables)
            await RunAsync(session.Connection, dialect switch
            {
                SqlDialect.SqlServer => $"IF OBJECT_ID('{schema}.{table}', 'U') IS NOT NULL DROP TABLE [{schema}].[{table}]",
                SqlDialect.Oracle => $"DROP TABLE IF EXISTS \"{schema}\".\"{table}\" CASCADE CONSTRAINTS PURGE",
                _ => $"DROP TABLE IF EXISTS \"{schema}\".\"{table}\"",
            }, ct);
        foreach (var statement in Schema.Create(dialect).Where(s => !s.StartsWith("CREATE INDEX", StringComparison.Ordinal)))
            await RunAsync(session.Connection, SqlTenantSchema.Qualify(statement, dialect, schema), ct);
    }

    private static async Task<bool> AttachedAsync(DbConnection connection, string schema, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_database_list WHERE name = @p0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@p0";
        parameter.Value = schema;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task RunAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
