using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Keyset pagination: <c>After(cursor)</c> continues an ordering from the last row's keys, and gives the same
///     pages as <c>Skip</c> on the seed, on the three databases, whatever the directions of the keys.
/// </summary>
[Collection("databases")]
public class KeysetTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task After_gives_the_pages_skip_gives(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);

        // Two keys, both ascending: a row-value comparison where the dialect has it.
        var ordered = session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(20).ToListAsync();
        var last = first[^1];
        var second = await ordered.After((last.CreatedAt, last.Id)).Take(20).ToListAsync();
        var offset = await ordered.Skip(20).Take(20).ToListAsync();
        Assert.Equal(20, second.Count);
        Assert.Equal(offset.Select(o => o.Id), second.Select(o => o.Id));

        // Every page by cursor walks the whole set once: the pages add up to the count, no row twice.
        var seen = new HashSet<Guid>();
        var cursor = (last.CreatedAt, last.Id);
        foreach (var row in first)
            seen.Add(row.Id);
        while (true)
        {
            var page = await ordered.After(cursor).Take(500).ToListAsync();
            if (page.Count == 0)
                break;
            foreach (var row in page)
                Assert.True(seen.Add(row.Id));
            cursor = (page[^1].CreatedAt, page[^1].Id);
        }

        Assert.Equal(await ordered.CountAsync(), seen.Count);

        // Mixed directions: the expanded form (k1 < a OR (k1 = a AND k2 > b)) on every dialect.
        var mixed = session.Query<Order>().Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).ThenBy(o => o.Id);
        var mixedFirst = await mixed.Take(20).ToListAsync();
        var mixedLast = mixedFirst[^1];
        Assert.Equal((await mixed.Skip(20).Take(20).ToListAsync()).Select(o => o.Id), (await mixed.After((mixedLast.Total, mixedLast.Id)).Take(20).ToListAsync()).Select(o => o.Id));

        // One key (the entity's): the cursor is the value itself.
        var byId = session.Query<Order>().OrderBy(o => o.Id);
        var byIdFirst = await byId.Take(20).ToListAsync();
        Assert.Equal((await byId.Skip(20).Take(20).ToListAsync()).Select(o => o.Id), (await byId.After(byIdFirst[^1].Id).Take(20).ToListAsync()).Select(o => o.Id));

        // A projection after the cursor, and the runtime path: the same rows.
        var rows = await ordered.After((last.CreatedAt, last.Id)).Take(20).Project<OrderRowDto>().ToListAsync();
        Assert.Equal(second.Select(o => o.Id), rows.Select(r => r.Id));
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var runtime = await composed.After((last.CreatedAt, last.Id)).Take(20).ToListAsync();
        Assert.Equal(second.Select(o => o.Id), runtime.Select(o => o.Id));
        var runtimeMixed = await composed.OrderByDescending(o => o.Total).ThenBy(o => o.Id).After((mixedLast.Total, mixedLast.Id)).Take(20).ToListAsync();
        Assert.Equal((await mixed.Skip(20).Take(20).ToListAsync()).Select(o => o.Id), runtimeMixed.Select(o => o.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Before_gives_the_previous_page_in_the_chains_order(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);

        // Forward with After, back with Before from the first row of the second page: the first page again, in the same order.
        var ordered = session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(20).ToListAsync();
        var second = await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(20).ToListAsync();
        var back = await ordered.Before((second[0].CreatedAt, second[0].Id)).Take(20).ToListAsync();
        Assert.Equal(first.Select(o => o.Id), back.Select(o => o.Id));

        // Without a Take: every row before the cursor, in order; FirstOrDefault: the row just before it; a stream: the same rows.
        var third = await ordered.After((second[^1].CreatedAt, second[^1].Id)).Take(20).ToListAsync();
        var cursor = (third[0].CreatedAt, third[0].Id);
        var all = await ordered.Before(cursor).ToListAsync();
        Assert.Equal(first.Concat(second).Select(o => o.Id), all.Select(o => o.Id));
        var nearest = await ordered.Before(cursor).FirstOrDefaultAsync();
        Assert.Equal(second[^1].Id, nearest!.Id);
        var streamed = new List<Guid>();
        await foreach (var row in ordered.Before(cursor).Take(20).AsAsyncEnumerable())
            streamed.Add(row.Id);
        Assert.Equal(second.Select(o => o.Id), streamed);

        // Mixed directions (the expanded form, each key's comparison inverted), and a collection include with its companion statement.
        var mixed = session.Query<Order>().Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).ThenBy(o => o.Id);
        var mixedFirst = await mixed.Take(20).ToListAsync();
        var mixedSecond = await mixed.After((mixedFirst[^1].Total, mixedFirst[^1].Id)).Take(20).ToListAsync();
        var mixedBack = await mixed.Before((mixedSecond[0].Total, mixedSecond[0].Id)).Take(20).ToListAsync();
        Assert.Equal(mixedFirst.Select(o => o.Id), mixedBack.Select(o => o.Id));
        var withLines = await ordered.Include(o => o.Lines).Before((second[0].CreatedAt, second[0].Id)).Take(20).ToListAsync();
        Assert.Equal(first.Select(o => o.Id), withLines.Select(o => o.Id));
        Assert.All(withLines, o => Assert.Equal(Seed.LinesPerOrder, o.Lines.Count));

        // The runtime path: the same page.
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var runtime = await composed.Before((second[0].CreatedAt, second[0].Id)).Take(20).ToListAsync();
        Assert.Equal(first.Select(o => o.Id), runtime.Select(o => o.Id));
        var runtimeAll = await composed.Before(cursor).ToListAsync();
        Assert.Equal(all.Select(o => o.Id), runtimeAll.Select(o => o.Id));

        // An API's tokens: next = the last row's keys signed, prev = the first row's; the client sends them back, the server pages either way.
        var signer = new SqlCursorSigner(new byte[32], "orders");
        var next = signer.Encode((first[^1].CreatedAt, first[^1].Id));
        var page2 = await ordered.After(signer.Decode<DateTimeOffset, Guid>(next)).Take(20).ToListAsync();
        Assert.Equal(second.Select(o => o.Id), page2.Select(o => o.Id));
        var prev = signer.Encode((page2[0].CreatedAt, page2[0].Id));
        var page1 = await ordered.Before(signer.Decode<DateTimeOffset, Guid>(prev)).Take(20).ToListAsync();
        Assert.Equal(first.Select(o => o.Id), page1.Select(o => o.Id));
    }
}
