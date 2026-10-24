using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     A predicate over a JSON array of objects: <c>d.Spec.Chapters.Any(c => condition)</c> is EXISTS over the array's elements (json_each,
///     jsonb_array_elements, OPENJSON, JSON_TABLE), the element's members read at their paths, with constants and the chain's argument.
///     Documents without the array or with an empty one take part, as they would in the shared table (MySQL's semijoin rewrite of the
///     correlated JSON_TABLE answered no rows: the rendering keeps it a subquery). The runtime path (IQueryable) renders the same.
/// </summary>
[Collection("databases")]
public class JsonObjectArrayTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Any_over_a_json_array_of_objects_tests_its_elements(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var batch = Guid.NewGuid().ToString("N")[..8];
        await session.InsertManyAsync(new[]
        {
            new Document { Id = Guid.NewGuid(), Name = batch + "-a", Spec = new DocumentSpec { Title = "Alpha", Tags = ["x", "y", "x"], Chapters = [new() { Heading = "Intro", Pages = 3 }, new() { Heading = "Body", Pages = 40 }] } },
            new Document { Id = Guid.NewGuid(), Name = batch + "-b", Spec = new DocumentSpec { Title = "Beta", Tags = ["z"], Chapters = [new() { Heading = "Intro", Pages = 2 }] } },
            new Document { Id = Guid.NewGuid(), Name = batch + "-c", Spec = new DocumentSpec { Title = "Gamma" } },
        });
        var prefix = batch + "-%";

        Assert.Equal(["Alpha"], await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p) && d.Spec.Chapters.Any(c => c.Pages > 10), prefix).Select(d => d.Spec.Title).ToListAsync());
        Assert.Equal(["Alpha", "Beta"], await session.Query<Document>().Where((d, a) => Sql.Like(d.Name, a.prefix) && d.Spec.Chapters.Any(c => c.Heading == a.heading), (prefix, heading: "Intro")).OrderBy(d => d.Spec.Title).Select(d => d.Spec.Title).ToListAsync());
        Assert.Equal(["Beta", "Gamma"], await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p) && !d.Spec.Chapters.Any(c => c.Pages >= 3), prefix).OrderBy(d => d.Spec.Title).Select(d => d.Spec.Title).ToListAsync());

        // Counted with a condition, through a Where, the distinct values of an array of values.
        Assert.Equal(["Alpha"], await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p) && d.Spec.Chapters.Count(c => c.Pages >= 2) == 2, prefix).Select(d => d.Spec.Title).ToListAsync());
        Assert.Equal(["Alpha", "Beta"], await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p) && d.Spec.Chapters.Where(c => c.Heading == "Intro").Any(), prefix).OrderBy(d => d.Spec.Title).Select(d => d.Spec.Title).ToListAsync());
        var counted = await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p), prefix).OrderBy(d => d.Spec.Title).Select(d => new { d.Spec.Title, Long = d.Spec.Chapters.Where(c => c.Pages > 2).Count(), Tags = d.Spec.Tags.Distinct().Count() }).ToListAsync();
        Assert.Equal([("Alpha", 2, 2), ("Beta", 0, 1), ("Gamma", 0, 0)], counted.Select(c => (c.Title, c.Long, c.Tags)));

        // The runtime path (an expression tree): the same EXISTS, a captured value inside the element's condition too.
        IQueryable<Document> runtime = session.Query<Document>();
        var heading = "Intro";
        Assert.Equal(1, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Chapters.Any(c => c.Pages > 10)).CountAsync());
        Assert.Equal(2, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Chapters.Any(c => c.Heading == heading && c.Pages < 5)).CountAsync());
        Assert.Equal(2, await runtime.Where(d => Sql.Like(d.Name, prefix) && !d.Spec.Chapters.Any(c => c.Pages >= 3)).CountAsync());
        Assert.Equal(1, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Chapters.Count(c => c.Pages >= 2) == 2).CountAsync());
        Assert.Equal(2, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Chapters.Where(c => c.Heading == heading).Count() > 0).CountAsync());
        Assert.Equal(1, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Tags.Distinct().Count() == 2).CountAsync());
        transaction.Rollback();
    }
}
