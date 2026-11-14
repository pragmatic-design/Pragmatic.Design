using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[Sort(Nulls = …)]</c> places the rows with a null key the same on the five databases, whatever the direction (#733).
/// </summary>
/// <remarks>
///     Without it each database orders nulls its own way: PostgreSQL and Oracle last ascending and first descending, SQL Server, MySQL and
///     SQLite the other way round. Three stretches of one person prefix: one closed on day 1, one on day 2, one open (no end).
/// </remarks>
[Collection("pragmatic databases")]
public class NullKeysGoWhereTheSortSaysTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Nulls_last_ascending(SqlDialect dialect)
    {
        var (prefix, rows) = await Seeded(dialect);
        Assert.Equal([prefix + "-day1", prefix + "-day2", prefix + "-open"], await Read(rows, new StaffByEndQuery { Person = prefix }));
    }

    /// <summary>Descending, where PostgreSQL and Oracle put nulls first by default.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Nulls_last_descending(SqlDialect dialect)
    {
        var (prefix, rows) = await Seeded(dialect);
        Assert.Equal([prefix + "-day2", prefix + "-day1", prefix + "-open"],
            await Read(rows, new StaffByEndQuery { Person = prefix, EndSort = SortDirection.Descending }));
    }

    /// <summary>Ascending, where SQL Server, MySQL and SQLite put nulls first by default and PostgreSQL and Oracle last.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Nulls_first_ascending(SqlDialect dialect)
    {
        var (prefix, rows) = await Seeded(dialect);
        Assert.Equal([prefix + "-open", prefix + "-day1", prefix + "-day2"], await Read(rows, new StaffByEndOpenFirstQuery { Person = prefix }));
    }

    private async Task<(string Prefix, SqlSession Session)> Seeded(SqlDialect dialect)
    {
        var prefix = "p" + Guid.NewGuid().ToString("N")[..8];
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), _toggle));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var property = new Property { Name = prefix, TenantId = "acme" };
            await session.InsertAsync(property);
            foreach (var (name, end) in new[] { ("open", (DateTimeOffset?)null), ("day2", Day1.AddDays(1)), ("day1", Day1) })
            {
                var stretch = new StaffAssignment { Person = prefix + "-" + name, ValidFrom = Day1.AddDays(-10), ValidTo = end };
                stretch.SetPropertyId(property.Id);
                await session.InsertAsync(stretch);
            }
        }

        return (prefix, session);
    }

    private async Task<string[]> Read<TQuery>(SqlSession session, TQuery query)
    {
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var repository = new StaffAssignment.SqlReadRepository(session);
            return query switch
            {
                StaffByEndQuery last => [.. (await repository.RunAsync(last)).Select(s => s.Person)],
                StaffByEndOpenFirstQuery first => [.. (await repository.RunAsync(first)).Select(s => s.Person)],
                _ => throw new ArgumentException("an unexpected query", nameof(query)),
            };
        }
    }
}
