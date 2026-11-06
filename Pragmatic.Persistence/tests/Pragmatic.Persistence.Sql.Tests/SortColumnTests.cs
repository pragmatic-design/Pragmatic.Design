using Pragmatic.Identity.Local;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A declared query's sorts order by the column the member is stored in, on the five databases (#564): <c>Id</c> by <c>PersistenceId</c>, and
///     an owned identity's <c>Email</c> by <c>Identity_Email</c>. Ordered by the member's name, the statement named a column the table does not have.
/// </summary>
[Collection("pragmatic databases")]
public class SortColumnTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_sort_on_the_key_orders_by_the_key_column(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var batch = Guid.NewGuid().ToString("N");
        foreach (var code in new[] { "a", "b", "c" })
            await session.InsertAsync(new Statement { Code = code, Batch = batch, IssuedAt = DateTimeOffset.UnixEpoch, Total = 1 });

        IReadRepository<Statement> statements = new Statement.SqlReadRepository(session);
        var ascending = await statements.RunAsync(new StatementsByBatchQuery { Batch = batch, IdSort = SortDirection.Ascending });
        var descending = await statements.RunAsync(new StatementsByBatchQuery { Batch = batch, IdSort = SortDirection.Descending });

        // Key order is the database's own (SQL Server orders a uniqueidentifier by its last bytes first): what proves the key is ordered by is that
        // the two directions are each other's reverse, over every row.
        Assert.Equal(["a", "b", "c"], ascending.Select(s => s.Code).Order());
        Assert.Equal(ascending.Select(s => s.Id), descending.Select(s => s.Id).Reverse());
        Assert.NotEqual(ascending.Select(s => s.Id), descending.Select(s => s.Id));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_sort_on_an_owned_member_orders_by_its_column(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var name = Guid.NewGuid().ToString("N");
        foreach (var email in new[] { "bruno@example.test", "carla@example.test", "anna@example.test" })
        {
            var account = Account.Register(new LocalIdentity { Email = name + "." + email, ExternalIdentityKey = "local|" + name + email, PasswordHash = "hash" });
            account.Name = name;
            await session.InsertAsync(account);
        }

        IReadRepository<Account> accounts = new Account.SqlReadRepository(session);
        var byEmail = await accounts.RunAsync(new AccountsByNameQuery { Name = name, EmailSort = SortDirection.Ascending });
        var byEmailDescending = await accounts.RunAsync(new AccountsByNameQuery { Name = name, EmailSort = SortDirection.Descending });

        Assert.Equal(["anna", "bruno", "carla"], byEmail.Select(a => a.Identity!.Email![(name.Length + 1)..].Split('@')[0]));
        Assert.Equal(["carla", "bruno", "anna"], byEmailDescending.Select(a => a.Identity!.Email![(name.Length + 1)..].Split('@')[0]));
    }
}
