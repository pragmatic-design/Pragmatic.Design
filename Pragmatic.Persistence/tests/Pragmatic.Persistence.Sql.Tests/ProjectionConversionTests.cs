using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A declared projection that formats a column and converts another on the five databases (#563): the summary the engine reads equals the
///     one the in-memory mapping makes of the same entity. Read with the DTO property's type, the date and the amount came back raw, or the read
///     failed: PostgreSQL refuses to read a timestamp as text.
/// </summary>
[Collection("pragmatic databases")]
public class ProjectionConversionTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_projection_formats_and_converts_as_the_mapping_does(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var statement = new Statement
        {
            Code = Guid.NewGuid().ToString("N"),
            IssuedAt = new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero),
            Total = 1234.5m,
        };
        await session.InsertAsync(statement);

        IReadRepository<Statement> statements = new Statement.SqlReadRepository(session);
        var summaries = await statements.RunAsync(new StatementSummariesQuery { Code = statement.Code });

        var expected = StatementSummary.FromEntity(statement);
        Assert.Equal(("2026-03-14", "1234.50 EUR"), (expected.IssuedDate, expected.TotalFormatted)); // the mapping's own answer, the setpoint
        var summary = Assert.Single(summaries);
        Assert.Equal((expected.Code, expected.IssuedDate, expected.TotalFormatted), (summary.Code, summary.IssuedDate, summary.TotalFormatted));
    }
}
