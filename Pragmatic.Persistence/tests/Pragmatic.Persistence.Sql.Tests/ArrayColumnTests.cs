using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>A bundle's array columns in a declared DTO: a List of strings or Guids is a column, not a collection of child DTOs (Showcase's AmenityDto.Keywords).</summary>
[MapFrom<Bundle>]
public sealed class BundleRow
{
    public Guid Id { get; set; }
    public List<string> Labels { get; set; } = [];
    public List<Guid> Refs { get; set; } = [];
    public int[] Scores { get; set; } = [];
}

/// <summary>
///     Array columns (<c>string[]</c>, <c>List&lt;Guid&gt;</c>, <c>int[]</c>): native arrays on PostgreSQL, their JSON text elsewhere; written and
///     read back whole, <c>Contains</c> on the column (<c>= ANY</c>, json_each, OPENJSON, JSON_CONTAINS, JSON_EXISTS) with a text, a Guid and a
///     number, the length in a predicate and a projection, an update of the array; the runtime path (IQueryable) the same. In a transaction,
///     rolled back.
/// </summary>
[Collection("databases")]
public class ArrayColumnTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Array_columns_are_written_read_and_queried(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var shared = Guid.NewGuid();
        var a = new Bundle { Id = Guid.NewGuid(), Labels = ["red", "it's \"quoted\""], Refs = [shared, Guid.NewGuid()], Scores = [3, 40] };
        var b = new Bundle { Id = Guid.NewGuid(), Labels = ["blue"], Refs = [shared], Scores = [] };
        await session.InsertManyAsync([a, b]);
        var ids = new[] { a.Id, b.Id };

        var read = (await session.Query<Bundle>().FirstOrDefaultAsync((x, id) => x.Id == id, a.Id))!;
        Assert.Equal(a.Labels, read.Labels);
        Assert.Equal(a.Refs, read.Refs);
        Assert.Equal(a.Scores, read.Scores);
        Assert.Empty((await session.Query<Bundle>().FirstOrDefaultAsync((x, id) => x.Id == id, b.Id))!.Scores);

        Assert.Equal([a.Id], (await session.Query<Bundle>().Where((x, p) => p.ids.Contains(x.Id) && x.Labels.Contains(p.label), (ids, label: "it's \"quoted\"")).ToListAsync()).Select(x => x.Id));
        Assert.Equal(2, await session.Query<Bundle>().CountAsync((x, p) => p.ids.Contains(x.Id) && x.Refs.Contains(p.shared), (ids, shared)));
        Assert.Equal([a.Id], (await session.Query<Bundle>().Where((x, p) => p.ids.Contains(x.Id) && x.Scores.Contains(p.score), (ids, score: 40)).ToListAsync()).Select(x => x.Id));
        Assert.Equal([b.Id], (await session.Query<Bundle>().Where((x, p) => p.Contains(x.Id) && x.Scores.Length == 0, ids).ToListAsync()).Select(x => x.Id));
        var lengths = await session.Query<Bundle>().Where((x, p) => p.Contains(x.Id), ids).OrderByDescending(x => x.Refs.Count).Select(x => new { x.Id, Labels = x.Labels.Length }).ToListAsync();
        Assert.Equal([(a.Id, 2), (b.Id, 1)], lengths.Select(l => (l.Id, l.Labels)));

        // The runtime path (an expression tree, IQueryable) renders the same: Contains on each kind, Length and Count.
        IQueryable<Bundle> runtime = session.Query<Bundle>();
        var label = "it's \"quoted\"";
        var score = 40;
        Assert.Equal([a.Id], (await runtime.Where(x => ids.Contains(x.Id) && x.Labels.Contains(label)).ToListAsync()).Select(x => x.Id));
        Assert.Equal(2, await runtime.Where(x => ids.Contains(x.Id) && x.Refs.Contains(shared)).CountAsync());
        Assert.Equal([a.Id], (await runtime.Where(x => ids.Contains(x.Id) && x.Scores.Contains(score)).ToListAsync()).Select(x => x.Id));
        Assert.Equal([b.Id], (await runtime.Where(x => ids.Contains(x.Id) && x.Scores.Length == 0).ToListAsync()).Select(x => x.Id));
        Assert.Equal([a.Id], (await runtime.Where(x => ids.Contains(x.Id) && x.Refs.Count > 1).ToListAsync()).Select(x => x.Id));

        // A declared DTO reads the arrays as its own types (string[] into a List<string>), compiled and at runtime.
        var row = (await session.Query<Bundle>().Where((x, id) => x.Id == id, a.Id).Project<BundleRow>().ToListAsync()).Single();
        var composedRow = (await runtime.Where(x => x.Id == a.Id).Project<Bundle, BundleRow>().ToListAsync()).Single();
        foreach (var projected in new[] { row, composedRow })
        {
            Assert.Equal(a.Labels, projected.Labels);
            Assert.Equal(a.Refs, projected.Refs);
            Assert.Equal(a.Scores, projected.Scores);
        }

        b.Scores = [7, 8, 9];
        await session.UpdateAsync(b);
        Assert.Equal(new[] { 7, 8, 9 }, (await session.Query<Bundle>().FirstOrDefaultAsync((x, id) => x.Id == id, b.Id))!.Scores);
        transaction.Rollback();
    }
}
