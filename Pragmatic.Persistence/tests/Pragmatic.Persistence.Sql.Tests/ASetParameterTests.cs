using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A specification whose list is a <c>HashSet</c> reads the rows it names on the five databases (#581). On PostgreSQL a list binds as an
///     array, and the set was handed to Npgsql as it was: it maps a <c>T[]</c> or a <c>List&lt;T&gt;</c>, and refused the set.
/// </summary>
[Collection("pragmatic databases")]
public class ASetParameterTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_set_of_keys_and_a_set_of_codes_read_the_rows_they_name(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var batch = Guid.NewGuid().ToString("N");
        var statements = new[] { "a", "b", "c" }.Select(c => new Statement { Code = batch + c, Batch = batch, IssuedAt = DateTimeOffset.UnixEpoch, Total = 1 }).ToList();
        foreach (var statement in statements)
            await session.InsertAsync(statement);

        IReadRepository<Statement> repository = new Statement.SqlReadRepository(session);
        var byKey = await repository.FindAsync(StatementSpecifications.Among([statements[0].PersistenceId, statements[2].PersistenceId]));
        var byCode = await repository.FindAsync(StatementSpecifications.Coded([batch + "b", batch + "c"]));
        var none = await repository.FindAsync(StatementSpecifications.Among([])); // an empty set: typed from its interface, it matches nothing

        Assert.Equal([batch + "a", batch + "c"], byKey.Select(s => s.Code).Order());
        Assert.Equal([batch + "b", batch + "c"], byCode.Select(s => s.Code).Order());
        Assert.Empty(none);
    }
}
