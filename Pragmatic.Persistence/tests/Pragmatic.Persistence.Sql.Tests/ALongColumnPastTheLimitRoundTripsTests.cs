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
