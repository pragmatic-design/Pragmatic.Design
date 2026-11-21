using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A column declared past MySQL's and Oracle's limits is created, written and read back whole on every database (#712).
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Archive" /> declares a 20,000-character text and a 3,000-byte binary. The engine wrote them as given, <c>varchar(20000)</c>
///         and <c>VARCHAR2(20000 CHAR)</c> / <c>RAW(3000)</c>, which MySQL and Oracle refuse to create, so the whole schema of this suite failed to
///         migrate there. They are now <c>longtext</c> on MySQL and <c>CLOB</c>/<c>BLOB</c> on Oracle.
///     </para>
///     <para>
///         Filtered by equality where the type allows it: Oracle compares no <c>CLOB</c> with <c>=</c>, which is the cost the column pays there.
///     </para>
/// </remarks>
[Collection("pragmatic databases")]
public class ALongColumnPastTheLimitRoundTripsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_and_a_binary_past_the_column_limits_are_read_back_whole(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var body = string.Concat(Enumerable.Range(0, 20000).Select(i => (char)('a' + i % 26)));
        var scan = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();

        var archive = new Archive { Body = body, Scan = scan };
        await session.InsertAsync(archive);

        var stored = (await session.Query<Archive>().ToListAsync()).Single(d => d.Id == archive.Id);
        Assert.Equal(body, stored.Body);
        Assert.Equal(scan, stored.Scan);
    }

    /// <summary>
    ///     A long text set by an update the session ran before with a short one: the plan's command is kept, so the second call reuses it
    ///     (#920). On SQL Server a kept command is prepared, and SqlClient sends no more of a value than the size it was prepared with.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_past_the_limit_set_by_a_kept_update_is_written_whole(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var archive = new Archive { Body = "short", Scan = [1, 2, 3] };
        await session.InsertAsync(archive);
        var body = new string('q', 6000);

        foreach (var value in new[] { "still short", body })
            await Rewrite(session, archive.Id, value);

        Assert.Equal(body, (await session.Query<Archive>().ToListAsync()).Single(d => d.Id == archive.Id).Body);
    }

    private static async Task Rewrite(SqlSession session, Guid id, string body)
        => await session.Query<Archive>().Where((d, key) => d.Id == key, id).ExecuteUpdateAsync((s, value) => s.SetProperty(d => d.Body, value), body);

    /// <summary>The writer's statement the same way: a second insert of the row type, past the limit, after a short one (#920).</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_past_the_limit_written_by_a_kept_insert_is_written_whole(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var body = new string('w', 6000);

        await session.InsertAsync(new Archive { Body = "short", Scan = [1, 2, 3] });
        var archive = new Archive { Body = body, Scan = [1, 2, 3] };
        await session.InsertAsync(archive);

        Assert.Equal(body, (await session.Query<Archive>().ToListAsync()).Single(d => d.Id == archive.Id).Body);
    }

    /// <summary>A plan's read the same way: the filter by a long text, after the same filter by a short one, finds the row (#920).</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_past_the_limit_is_found_by_a_kept_read(SqlDialect dialect)
    {
        if (dialect == SqlDialect.Oracle)
            return; // Oracle compares no CLOB with =: the test above says so, and this one has nothing to find there
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var body = new string('r', 6000);
        var archive = new Archive { Body = body, Scan = [1, 2, 3] };
        await session.InsertAsync(archive);

        // The plan's first call, with a short value no row holds (the archives are not emptied between tests, so not "short").
        Assert.Empty(await Find(session, $"short-{Guid.NewGuid():N}"));
        Assert.Contains(await Find(session, body), d => d.Id == archive.Id);
    }

    private static async Task<List<Archive>> Find(SqlSession session, string body)
        => await session.Query<Archive>().Where((d, value) => d.Body == value, body).ToListAsync();

    /// <summary>
    ///     Filtered by its value: found where the database compares the type with <c>=</c>, and refused on Oracle, which compares no <c>CLOB</c>
    ///     (ORA-00932), the documented cost of the large type there.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_past_the_limit_is_found_by_its_value_or_refused_on_Oracle(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var body = new string('q', 18000);
        var archive = new Archive { Body = body, Scan = [1, 2, 3] };
        await session.InsertAsync(archive);

        if (dialect == SqlDialect.Oracle)
        {
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => session.Query<Archive>().Where((d, value) => d.Body == value, body).ToListAsync().AsTask());
            return;
        }

        var found = await session.Query<Archive>().Where((d, value) => d.Body == value, body).ToListAsync();

        Assert.Contains(found, d => d.Id == archive.Id);
    }
}
