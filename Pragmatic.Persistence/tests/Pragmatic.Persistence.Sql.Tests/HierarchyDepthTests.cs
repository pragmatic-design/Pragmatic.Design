using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A hierarchy in one table three levels deep (Charge → CardCharge → PremiumCardCharge): every level reads the rows of its subtree as
///     their own types with the columns of their whole lineage, OfType and <c>is</c> take the subtree, and every level's writer writes a row
///     of a deeper type with its own discriminator and columns. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class HierarchyDepthTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Every_level_reads_and_writes_its_subtree_as_its_own_types(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var marker = "depth-" + Guid.NewGuid().ToString("N")[..8];
        await session.InsertAsync<Charge>(new CardCharge { Id = Guid.NewGuid(), Amount = 1m, Description = marker, Last4 = "1111" });
        await session.InsertAsync<Charge>(new PremiumCardCharge { Id = Guid.NewGuid(), Amount = 2m, Description = marker, Last4 = "2222", Tier = 2 }); // the root's writer
        await session.InsertAsync<CardCharge>(new PremiumCardCharge { Id = Guid.NewGuid(), Amount = 3m, Description = marker, Last4 = "3333", Tier = 3 }); // the intermediate's
        await session.InsertAsync(new PremiumCardCharge { Id = Guid.NewGuid(), Amount = 4m, Description = marker, Last4 = "4444", Tier = 4 }); // the leaf's
        await session.InsertAsync<Charge>(new WireCharge { Id = Guid.NewGuid(), Amount = 5m, Description = marker, Iban = "IT01", Days = 1 });

        static string Show(Charge c) => c switch
        {
            PremiumCardCharge p => $"P:{p.Last4}:{p.Tier}",
            CardCharge k => $"C:{k.Last4}",
            WireCharge w => $"W:{w.Iban}",
            _ => "R",
        };

        var all = await session.Query<Charge>().Where((c, m) => c.Description == m, marker).OrderBy(c => c.Amount).ToListAsync();
        Assert.Equal(["C:1111", "P:2222:2", "P:3333:3", "P:4444:4", "W:IT01"], all.Select(Show));
        var cards = await session.Query<CardCharge>().Where((c, m) => c.Description == m, marker).OrderBy(c => c.Amount).ToListAsync();
        Assert.Equal(["C:1111", "P:2222:2", "P:3333:3", "P:4444:4"], cards.Select(Show));
        var premium = await session.Query<PremiumCardCharge>().Where((c, m) => c.Description == m, marker).OrderBy(c => c.Amount).ToListAsync();
        Assert.Equal(["P:2222:2", "P:3333:3", "P:4444:4"], premium.Select(Show));

        Assert.Equal(4, (await session.Query<Charge>().Where((c, m) => c.Description == m, marker).OfType<CardCharge>().ToListAsync()).Count);
        Assert.Equal(4, await session.Query<Charge>().CountAsync((c, m) => c.Description == m && c is CardCharge, marker));
        Assert.Equal(3, await session.Query<Charge>().CountAsync((c, m) => c.Description == m && c is PremiumCardCharge, marker));

        // An update through the root keeps the deeper type's columns.
        var second = (PremiumCardCharge)all[1];
        second.Tier = 9;
        await session.UpdateAsync<Charge>(second);
        Assert.Equal(9, (await session.Query<PremiumCardCharge>().FirstOrDefaultAsync((c, id) => c.Id == id, second.Id))!.Tier);
        transaction.Rollback();
    }
}
