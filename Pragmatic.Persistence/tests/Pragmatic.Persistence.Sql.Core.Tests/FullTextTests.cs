using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Full text: <c>Sql.FullText(column, query)</c> as a predicate and <c>Sql.FullTextRank</c> in an ordering, on a <c>[FullText]</c> column. Native on
///     PostgreSQL (<c>to_tsvector @@ plainto_tsquery</c>, <c>ts_rank</c>), SQLite (FTS5, bm25), SQL Server (<c>CONTAINS</c>, <c>CONTAINSTABLE</c>'s
///     rank), MySQL (<c>MATCH … AGAINST</c>) and Oracle (<c>CONTAINS</c>, <c>SCORE</c>). The same rows and the same first row everywhere for a
///     one-word query. SQL Server's, MySQL's and Oracle's indexes see committed rows (SQL Server's once populated): there the rows are committed,
///     SQL Server waits on the server's population state, and the rows are deleted after.
/// </summary>
[Collection("databases")]
public class FullTextTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Full_text_matches_and_ranks(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var committed = dialect is SqlDialect.SqlServer or SqlDialect.MySql or SqlDialect.Oracle; // the full-text index reads committed rows
        using var transaction = committed ? null : session.BeginTransaction();
        var project = Guid.NewGuid();
        var tickets = new[]
        {
            Ticket("Urgent: the pump is leaking", project),
            Ticket("Routine check of the valves", project),
            Ticket("URGENT urgent escalation on the pump", project),
            Ticket("Pump replaced, closing", project),
        };
        await session.InsertManyAsync(tickets);
        try
        {
            if (dialect == SqlDialect.SqlServer)
                await WaitForPopulationAsync(session); // populated after the commit, in the background

            var matching = await session.Query<Ticket>().Where((t, q) => t.ProjectId == q.Project && Sql.FullText(t.Title, q.Word), (Project: project, Word: "urgent")).Select(t => t.Title).ToListAsync();
            Assert.Equal(2, matching.Count);
            Assert.All(matching, title => Assert.Contains("urgent", title, StringComparison.OrdinalIgnoreCase));

            var ranked = await session.Query<Ticket>().Where((t, q) => t.ProjectId == q.Project && Sql.FullText(t.Title, q.Word), (Project: project, Word: "urgent"))
                .OrderByDescending((t, q) => Sql.FullTextRank(t.Title, q), "urgent").ThenBy(t => t.Title).Select(t => t.Title).ToListAsync();
            Assert.Equal("URGENT urgent escalation on the pump", ranked[0]); // twice the word: first on every engine

            var pump = await session.Query<Ticket>().CountAsync((t, q) => t.ProjectId == q.Project && Sql.FullText(t.Title, q.Word), (Project: project, Word: "pump"));
            Assert.Equal(3, pump);

            // Several words: every one required on every engine (PostgreSQL's plainto_tsquery semantics), punctuation ignored.
            var both = await session.Query<Ticket>().CountAsync((t, q) => t.ProjectId == q.Project && Sql.FullText(t.Title, q.Words), (Project: project, Words: "pump, urgent"));
            Assert.Equal(2, both);

            // The runtime path renders the same native forms from the column's metadata.
            var runtime = await session.Query<Ticket>().AsQueryable().Where(t => t.ProjectId == project && Sql.FullText(t.Title, "pump")).CountAsync();
            Assert.Equal(3, runtime);
            var sql = session.Query<Ticket>().Where((t, q) => Sql.FullText(t.Title, q), "pump").ToSql();
            if (dialect == SqlDialect.Sqlite)
                Assert.Contains("\"Tickets_Title_fts\" MATCH", sql, StringComparison.Ordinal);
            if (dialect == SqlDialect.SqlServer)
                Assert.Contains("CONTAINS(t.[Title]", sql, StringComparison.Ordinal);
            if (dialect == SqlDialect.MySql)
                Assert.Contains("MATCH(t.\"Title\") AGAINST", sql, StringComparison.Ordinal);
            if (dialect == SqlDialect.Oracle)
                Assert.Contains("CONTAINS(t.\"Title\"", sql, StringComparison.Ordinal);
        }
        finally
        {
            if (committed)
            {
                foreach (var ticket in tickets)
                    await session.DeleteAsync(ticket);
            }
            else
                transaction!.Rollback();
        }
    }

    /// <summary>SQL Server: until the table's full-text index has no pending change and no population running, bounded at 60 s.</summary>
    private static async Task WaitForPopulationAsync(SqlSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            using var state = session.Connection.CreateCommand();
            state.CommandText = "SELECT CAST(OBJECTPROPERTYEX(OBJECT_ID('Tickets'), 'TableFullTextPendingChanges') AS int) + CAST(OBJECTPROPERTYEX(OBJECT_ID('Tickets'), 'TableFullTextPopulateStatus') AS int)";
            if (Convert.ToInt32(await state.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0)
                return;
            Assert.True(DateTime.UtcNow < deadline, "SQL Server's full-text index did not finish populating within 60 s");
            await Task.Delay(200);
        }
    }

    private static Ticket Ticket(string title, Guid project) => new() { Id = Guid.NewGuid(), ProjectId = project, Title = title, Description = "", Amount = 0m, Priority = 1, CreatedAt = DateTimeOffset.UtcNow };
}
