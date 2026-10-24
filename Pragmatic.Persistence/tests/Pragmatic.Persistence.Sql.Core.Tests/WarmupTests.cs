using System.Diagnostics;
using Pragmatic.Persistence.Workload;
using Xunit;
using Xunit.Abstractions;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     <c>session.WarmupAsync()</c>: every plan the generator registered (lazily, at module start) and every entity's base writes
///     get their command prepared on the session ahead of the first call; the first call then binds and runs, no PREPARE round trip.
/// </summary>
[Collection("databases")]
public class WarmupTests(TestDatabases databases, ITestOutputHelper output)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Fact]
    public void Every_plan_of_the_loaded_assemblies_is_registered_once()
    {
        var plans = SqlPlans.All.Select(p => p()).ToList();
        Assert.True(plans.Count > 100, plans.Count.ToString()); // this assembly's call sites, the workload's [Query] definitions and hierarchy walks
        Assert.Equal(plans.Count, plans.Select(p => p.Id).Distinct().Count());
        Assert.Contains(plans, p => !p.Preparable); // a plan with an IN list expands per call: not prepared ahead
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Warmup_prepares_every_plan_and_writer_once_and_the_first_calls_run_on_them(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customer = seed.BusyCustomer;

        // Cold: the first call of a handful of plans, each paying its own prepare.
        using (var cold = databases.Session(dialect))
        {
            var watch = Stopwatch.StartNew();
            await Calls(cold, customer);
            output.WriteLine($"{dialect} cold first calls: {watch.Elapsed.TotalMilliseconds:F1} ms");
        }

        // Warm: everything prepared first, then the same first calls.
        using var session = databases.Session(dialect);
        var warming = Stopwatch.StartNew();
        var warmed = await session.WarmupAsync();
        output.WriteLine($"{dialect} warmup: {warmed.Prepared} commands in {warming.Elapsed.TotalMilliseconds:F1} ms, {warmed.Skipped.Count} skipped");
        var plans = SqlPlans.All.Select(p => p()).ToList();
        var preparable = plans.Count(p => p.Preparable && p.Supports(dialect) && (dialect != SqlDialect.MySql || !p.Sql(dialect).Contains("; ", StringComparison.Ordinal))); // MySQL: a plan without a statement there, or of several statements, is not prepared
        Assert.True(warmed.Prepared + warmed.Skipped.Count >= preparable, $"{warmed.Prepared} + {warmed.Skipped.Count} < {preparable}");
        // SQLite has no REGEXP unless the application registers it on the connection (these sessions do not) and no functions (orders_of): those plans are left to their first call, the others warm.
        if (dialect == SqlDialect.Sqlite)
            Assert.All(warmed.Skipped, s => Assert.True(s.Error.Message.Contains("REGEXP", StringComparison.Ordinal) || s.Error.Message.Contains("orders_of", StringComparison.Ordinal) || s.Error.Message.Contains("vec_", StringComparison.Ordinal), s.Error.Message)); // the vector distances are the application's functions on SQLite too
        else if (dialect == SqlDialect.MySql)
            Assert.All(warmed.Skipped, s => Assert.Contains("vec_", s.Error.Message, StringComparison.Ordinal)); // the vector distances are stored functions the application defines on MySQL
        else
            Assert.Empty(warmed.Skipped);
        var skippedIds = warmed.Skipped.Select(s => s.Plan.Id).ToHashSet();
        foreach (var plan in plans.Where(p => p.Preparable && p.Supports(dialect) && !skippedIds.Contains(p.Id) && (dialect != SqlDialect.MySql || !p.Sql(dialect).Contains("; ", StringComparison.Ordinal))))
            Assert.NotNull(session.CommandFor(plan.Id));
        if (dialect == SqlDialect.PostgreSql)
            Assert.All(SqlPlans.All.Select(p => p()).Where(p => p.Preparable).Select(p => session.CommandFor(p.Id)), c => Assert.True(((global::Npgsql.NpgsqlCommand)c!).IsPrepared));
        Assert.Equal(0, (await session.WarmupAsync()).Prepared); // nothing left to prepare

        var watch2 = Stopwatch.StartNew();
        var rows = await Calls(session, customer);
        output.WriteLine($"{dialect} warmed first calls: {watch2.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == customer.Id && !o.IsDeleted), rows);
    }

    /// <summary>Five plans of this assembly, first calls: the rows they read, as a check against the seed.</summary>
    private static async Task<int> Calls(SqlSession session, Customer customer)
    {
        var live = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id).CountAsync();
        _ = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customer.Id).OrderBy(o => o.Number).Take(5).Select(o => o.Number).ToListAsync();
        _ = await session.Query<Customer>().FirstOrDefaultAsync((c, id) => c.Id == id, customer.Id);
        _ = await session.Query<OrderLine>().Where((l, id) => l.Order!.CustomerId == id, customer.Id).CountAsync();
        _ = await session.Query<Order>().Include(o => o.Lines).Where((o, id) => o.CustomerId == id, customer.Id).Take(2).ToListAsync();
        return live;
    }
}
