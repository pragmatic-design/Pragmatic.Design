using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     JSON in the expressions: a member path below a JSON column is the value at that path (cast to the member's kind), in predicates,
///     projections and orderings; an array's Contains and Count; a member set in an update (json_set / jsonb_set / JSON_MODIFY). The
///     names follow <c>[JsonPropertyName]</c>; the runtime path renders the same.
/// </summary>
[Collection("databases")]
public class JsonTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Json_paths_render_per_dialect()
    {
        // A number, a text, a nested member under its serialized name, an array's Contains and Count.
        var postgres = Session(SqlDialect.PostgreSql).Query<Document>().Where((d, a) => d.Spec.Pages > a.pages && d.Spec.Title == a.title && d.Spec.Author!.Name == a.author && d.Spec.Tags.Contains(a.tag) && d.Spec.Tags.Count >= 1, (pages: 40, title: "x", author: "y", tag: "pdf")).OrderBy(d => d.Spec.Title).ToSql();
        Assert.EndsWith(" WHERE (((((CAST(t.\"Spec\" ->> 'Pages' AS integer) > @p0) AND ((t.\"Spec\" ->> 'Title') = @p1)) AND ((t.\"Spec\" #>> '{Author,full_name}') = @p2)) AND (EXISTS (SELECT 1 FROM jsonb_array_elements_text(t.\"Spec\" #> '{Tags}') e WHERE e.value = @p3))) AND (jsonb_array_length(t.\"Spec\" #> '{Tags}') >= @p4)) ORDER BY (t.\"Spec\" ->> 'Title')", postgres, StringComparison.Ordinal);
        var sqlite = Session(SqlDialect.Sqlite).Query<Document>().Where((d, a) => d.Spec.Pages > a.pages && d.Spec.Author!.Name == a.author && d.Spec.Tags.Contains(a.tag), (pages: 40, author: "y", tag: "pdf")).ToSql();
        Assert.EndsWith(" WHERE (((json_extract(t.\"Spec\", '$.Pages') > @p0) AND (json_extract(t.\"Spec\", '$.Author.full_name') = @p1)) AND (EXISTS (SELECT 1 FROM json_each(t.\"Spec\", '$.Tags') WHERE json_each.value = @p2)))", sqlite, StringComparison.Ordinal);
        var sqlServer = Session(SqlDialect.SqlServer).Query<Document>().Where((d, a) => d.Spec.Pages > a.pages && d.Spec.Tags.Contains(a.tag), (pages: 40, tag: "pdf")).Select(d => new { d.Name, d.Spec.Title, Tags = d.Spec.Tags.Count }).ToSql();
        Assert.Equal("SELECT t.[Name], JSON_VALUE(t.[Spec], '$.Title'), (SELECT COUNT(*) FROM OPENJSON(t.[Spec], '$.Tags')) FROM [Documents] t WHERE ((CAST(JSON_VALUE(t.[Spec], '$.Pages') AS int) > @p0) AND (EXISTS (SELECT 1 FROM OPENJSON(t.[Spec], '$.Tags') WHERE value = @p1)))", sqlServer);

        // The runtime path renders the same predicate.
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Document> composed = session.Query<Document>();
        var tag = "pdf";
        Assert.Equal(session.Query<Document>().Where((d, a) => d.Spec.Pages > a.pages && d.Spec.Author!.Name == a.author && d.Spec.Tags.Contains(a.tag) && d.Spec.Tags.Count > 0, (pages: 40, author: "y", tag)).ToSql(),
            composed.Where(d => d.Spec.Pages > 40 && d.Spec.Author!.Name == "y" && d.Spec.Tags.Contains(tag) && d.Spec.Tags.Count > 0).ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Json_paths_filter_project_order_and_update_what_the_documents_hold(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var batch = Guid.NewGuid().ToString("N")[..8];
        var documents = new[]
        {
            new Document { Id = Guid.NewGuid(), Name = batch + "-a", Spec = new DocumentSpec { Title = "Alpha", Tags = ["pdf", "v2"], Pages = 42, Author = new DocumentAuthor { Name = "Ada" } } },
            new Document { Id = Guid.NewGuid(), Name = batch + "-b", Spec = new DocumentSpec { Title = "Beta", Tags = ["doc"], Pages = 7, Author = new DocumentAuthor { Name = "Bob" } } },
            new Document { Id = Guid.NewGuid(), Name = batch + "-c", Spec = new DocumentSpec { Title = "Gamma", Tags = [], Pages = 100 } },
        };
        await session.InsertManyAsync(documents);
        var prefix = batch + "-%";

        Assert.Equal(["Alpha", "Gamma"], await session.Query<Document>().Where((d, a) => Sql.Like(d.Name, a.prefix) && d.Spec.Pages > a.pages, (prefix, pages: 10)).OrderBy(d => d.Spec.Title).Select(d => d.Spec.Title).ToListAsync());
        Assert.Equal(["Alpha"], await session.Query<Document>().Where((d, a) => Sql.Like(d.Name, a.prefix) && d.Spec.Tags.Contains(a.tag), (prefix, tag: "pdf")).Select(d => d.Spec.Title).ToListAsync());
        Assert.Equal(["Ada"], await session.Query<Document>().Where((d, a) => Sql.Like(d.Name, a.prefix) && d.Spec.Author!.Name == a.author, (prefix, author: "Ada")).Select(d => d.Spec.Author!.Name).ToListAsync());
        var shaped = await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p), prefix).OrderByDescending(d => d.Spec.Pages).Select(d => new { d.Spec.Title, d.Spec.Pages, Tags = d.Spec.Tags.Count }).ToListAsync();
        Assert.Equal([("Gamma", 100, 0), ("Alpha", 42, 2), ("Beta", 7, 1)], shaped.Select(s => (s.Title, s.Pages, s.Tags)));
        IQueryable<Document> runtime = session.Query<Document>();
        Assert.Equal(2, await runtime.Where(d => Sql.Like(d.Name, prefix) && d.Spec.Pages > 10).CountAsync());

        // A member of the document set in place: the other members stay; read back through the converter.
        var touched = await session.Query<Document>().Where((d, p) => Sql.Like(d.Name, p), prefix).Where((d, t) => d.Spec.Title == t, "Beta").ExecuteUpdateAsync((s, pages) => s.SetProperty(d => d.Spec.Pages, pages), 8);
        Assert.Equal(1, touched);
        var beta = await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, documents[1].Id);
        Assert.Equal((8, "Beta", "Bob"), (beta!.Spec.Pages, beta.Spec.Title, beta.Spec.Author!.Name));
        Assert.Equal(["doc"], beta.Spec.Tags);
    }
}
