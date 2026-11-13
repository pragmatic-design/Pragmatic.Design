using System.Data.Common;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A string longer than 4000 characters is written whole where its column holds it, and refused where it does not; never cut (#717).
/// </summary>
/// <remarks>
///     Every string parameter was typed with a size of 4000, which is the most SqlClient, Npgsql, Microsoft.Data.Sqlite and ODP.NET send of a
///     value: a 5000-character review text read back as its first 4000 on SQLite, PostgreSQL, SQL Server and Oracle, with no error. MySQL's
///     driver ignores the size and kept it. An unbounded string is <c>nvarchar(max)</c> / <c>text</c> / <c>longtext</c>, which hold it, and
///     <c>VARCHAR2(4000 CHAR)</c> on Oracle, which does not.
/// </remarks>
[Collection("pragmatic databases")]
public class ALongTextRoundTripsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_of_5000_characters_is_read_back_whole_or_refused(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var text = new string('x', 5000);
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var property = new Property { Name = "RIVA", TenantId = "acme" };
            property.GrantScope("desk-north");
            await session.InsertAsync(property);
            var review = new Review { Text = text, Published = true, Stars = 1 };
            review.SetPropertyId(property.Id);
            review.SetOwnerId("anna");

            if (dialect == SqlDialect.Oracle)
            {
                await Assert.ThrowsAnyAsync<DbException>(() => session.InsertAsync(review));
                return;
            }

            await session.InsertAsync(review);
            var stored = (await session.Query<Review>().ToListAsync()).Single();
            Assert.Equal(5000, stored.Text.Length);
        }
    }

    /// <summary>An update writes the long text whole too: the statement the writer prepared for a short one widens for it.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_updated_to_5000_characters_is_read_back_whole_or_refused(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var property = new Property { Name = "RIVA", TenantId = "acme" };
            property.GrantScope("desk-north");
            await session.InsertAsync(property);
            var review = new Review { Text = "short", Published = true, Stars = 1 };
            review.SetPropertyId(property.Id);
            review.SetOwnerId("anna");
            await session.InsertAsync(review);

            review.Text = new string('y', 5000);
            if (dialect == SqlDialect.Oracle)
            {
                await Assert.ThrowsAnyAsync<DbException>(() => session.UpdateAsync(review));
                return;
            }

            await session.UpdateAsync(review);
            Assert.Equal(5000, (await session.Query<Review>().ToListAsync()).Single().Text.Length);
        }
    }

    /// <summary>The control: a string that fits is written whole on every database, Oracle's included.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_text_of_4000_characters_is_read_back_whole(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var property = new Property { Name = "RIVA", TenantId = "acme" };
            property.GrantScope("desk-north");
            await session.InsertAsync(property);
            var review = new Review { Text = new string('x', 4000), Published = true, Stars = 1 };
            review.SetPropertyId(property.Id);
            review.SetOwnerId("anna");
            await session.InsertAsync(review);

            Assert.Equal(4000, (await session.Query<Review>().ToListAsync()).Single().Text.Length);
        }
    }
}
