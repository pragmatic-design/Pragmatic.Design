using Pragmatic.Persistence.Workload;
using Xunit;
using Visible = Pragmatic.Persistence.Sql.Tests.Visibility;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A <c>[VisibleWhen]</c> rule on the root of a hierarchy in one table holds for the derived types: <c>Query&lt;WireCharge&gt;()</c> and
///     <c>OfType&lt;WireCharge&gt;()</c> carry the root's predicate next to the discriminator's, as EF Core's query filters do;
///     <c>WithoutFilters()</c> drops it. Rows written through the workload's types, read through the test-only ones, rolled back.
/// </summary>
[Collection("databases")]
public class VisibleHierarchyTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_rule_on_the_root_holds_for_the_derived_types(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var marker = "visible-" + Guid.NewGuid().ToString("N")[..8];
        await session.InsertAsync<Charge>(new CardCharge { Id = Guid.NewGuid(), Amount = 10m, Description = marker, Last4 = "1111" });
        await session.InsertAsync<Charge>(new CardCharge { Id = Guid.NewGuid(), Amount = 30m, Description = marker, Last4 = "2222" });
        await session.InsertAsync<Charge>(new WireCharge { Id = Guid.NewGuid(), Amount = 20m, Description = marker, Iban = "IT01", Days = 1 });
        await session.InsertAsync<Charge>(new WireCharge { Id = Guid.NewGuid(), Amount = 5m, Description = marker, Iban = "IT02", Days = 2 });

        var wires = await session.Query<Visible.WireCharge>().Where((w, m) => w.Description == m, marker).ToListAsync();
        Assert.Equal(["IT01"], wires.Select(w => w.Iban)); // the root's rule (Amount > 15) on the derived type
        Assert.Equal(1, await session.Query<Visible.CardCharge>().CountAsync((c, m) => c.Description == m, marker));
        Assert.Equal(["IT01"], (await session.Query<Visible.Charge>().Where((c, m) => c.Description == m, marker).OfType<Visible.WireCharge>().ToListAsync()).Select(w => w.Iban));
        Assert.Equal(2, await session.Query<Visible.Charge>().CountAsync((c, m) => c.Description == m, marker));
        Assert.Equal(2, await session.Query<Visible.WireCharge>().WithoutFilters().CountAsync((w, m) => w.Description == m, marker));
        Assert.Contains("> ", session.Query<Visible.WireCharge>().Where((w, m) => w.Description == m, marker).ToSql(), StringComparison.Ordinal);
        transaction.Rollback();
    }
}
