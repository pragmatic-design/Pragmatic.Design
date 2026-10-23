using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     SaveAsync over the entity's own change tracking (IChangeTracking, the repository's contract): a new row inserted, a modified
///     one updated in its modified columns only (the statement says so), the version checked and advanced, an untouched one left
///     alone, the tracking reset after; many rows in runs of the same columns, one command per run, in a transaction when more.
/// </summary>
[Collection("databases")]
public class SaveTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlWriter<T> WriterOf<T>() => SqlEntityMap<T>.Current.Writer!;

    private static Ticket NewTicket(Guid project, int i) => new() { Id = Guid.NewGuid(), ProjectId = project, Title = "New", Description = $"Ticket {i}", Amount = 10m + i, Priority = i % 5, CreatedAt = DateTimeOffset.UnixEpoch, IsNew = true };

    [Fact]
    public void The_save_statement_names_the_modified_columns_only()
    {
        var title = new HashSet<string> { "Title" };
        Assert.Equal("UPDATE \"Tickets\" SET \"Title\" = @p0 WHERE \"Id\" = @p1", WriterOf<Ticket>().SaveSql(SqlDialect.PostgreSql, title));
        Assert.Equal("UPDATE [Tickets] SET [Title] = @p0, [Priority] = @p1 WHERE [Id] = @p2", WriterOf<Ticket>().SaveSql(SqlDialect.SqlServer, new HashSet<string> { "Priority", "Title" }));
        // With a version column: advanced in the SET, checked in the WHERE, never a modified column itself.
        Assert.Equal("UPDATE \"Events\" SET \"Kind\" = @p0, \"Version\" = \"Version\" + 1 WHERE \"Id\" = @p1 AND \"Version\" = @p2", WriterOf<Event>().SaveSql(SqlDialect.Sqlite, new HashSet<string> { "Kind", "Version" }));
        // Many rows of the same columns: one command, the parameters numbered through.
        Assert.Equal("UPDATE \"Tickets\" SET \"Title\" = @p0 WHERE \"Id\" = @p1; UPDATE \"Tickets\" SET \"Title\" = @p2 WHERE \"Id\" = @p3", WriterOf<Ticket>().SaveSql(SqlDialect.PostgreSql, title, rows: 2));
        // Nothing modified: nothing to set (the save itself returns false before building any statement).
        Assert.StartsWith("UPDATE \"Tickets\" SET  WHERE", WriterOf<Ticket>().SaveSql(SqlDialect.PostgreSql, new HashSet<string>()), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_save_inserts_a_new_row_updates_the_modified_columns_and_resets_the_tracking(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var project = Guid.NewGuid();

        // New: inserted, no longer new, nothing modified.
        var ticket = NewTicket(project, 1);
        Assert.True(await session.SaveAsync(ticket));
        Assert.False(ticket.IsNew);
        Assert.Empty(ticket.ModifiedProperties);
        var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Tickets, t => t.Id == ticket.Id);
        Assert.Equal(("New", "Ticket 1", 11m, 1), (stored.Title, stored.Description, stored.Amount, stored.Priority));

        // Untouched: nothing written. A plain setter is not tracked (the repository's generated Set{Property} methods are): the column stays.
        Assert.False(await session.SaveAsync(ticket));
        ticket.Description = "untracked";
        Assert.False(await session.SaveAsync(ticket));

        // Modified through the tracked setters: those columns only; the untracked change does not reach the row.
        ticket.SetTitle("Open");
        ticket.SetPriority(4);
        Assert.True(await session.SaveAsync(ticket));
        Assert.Empty(ticket.ModifiedProperties);
        ef.ChangeTracker.Clear();
        var updated = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Tickets, t => t.Id == ticket.Id);
        Assert.Equal(("Open", "Ticket 1", 4), (updated.Title, updated.Description, updated.Priority));

        // With a version: advanced on the object as on the row; someone else's copy at the old version is refused.
        var created = new Event { Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Kind = "created", At = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero), IsNew = true };
        Assert.True(await session.SaveAsync(created));
        created.SetKind("paid");
        Assert.True(await session.SaveAsync(created));
        Assert.Equal(1, created.Version);
        var stale = new Event { Id = created.Id, OrderId = created.OrderId, Kind = "created", At = created.At, Version = 0 };
        stale.SetKind("cancelled");
        await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.SaveAsync(stale));
        ef.ChangeTracker.Clear();
        var paid = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Events, e => e.Id == created.Id);
        Assert.Equal(("paid", 1), (paid.Kind, paid.Version));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Many_rows_save_in_runs_of_the_same_columns(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var project = Guid.NewGuid();
        var tickets = Enumerable.Range(0, 300).Select(i => NewTicket(project, i)).ToList();
        Assert.Equal(300, await session.SaveManyAsync(tickets)); // a batch of inserts (two commands: 256 + 44, one transaction)
        Assert.All(tickets, t => Assert.False(t.IsNew));

        // 100 new rows, 150 with the same column, 10 with two columns, 40 untouched: inserts, two runs, the rest skipped.
        var more = Enumerable.Range(300, 100).Select(i => NewTicket(project, i)).ToList();
        for (var i = 0; i < 150; i++)
            tickets[i].SetTitle("Open");
        for (var i = 150; i < 160; i++)
        {
            tickets[i].SetTitle("Closed");
            tickets[i].SetAmount(0m);
        }

        var all = tickets.Concat(more).ToList();
        Assert.Equal(260, await session.SaveManyAsync(all));
        Assert.All(all, t => Assert.Empty(t.ModifiedProperties));
        var byTitle = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(ef.Tickets.Where(t => t.ProjectId == project));
        Assert.Equal(400, byTitle.Count);
        Assert.Equal(150, byTitle.Count(t => t.Title == "Open"));
        Assert.Equal(10, byTitle.Count(t => t.Title == "Closed" && t.Amount == 0m));
        Assert.Equal(240, byTitle.Count(t => t.Title == "New"));

        // A batch with a version: one stale row among the sent ones is a conflict for the batch (named on SQLite, where each row is its own statement), and the transaction leaves nothing of it.
        var orderId = Guid.NewGuid();
        var events = Enumerable.Range(0, 3).Select(i => new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "e" + i, At = DateTimeOffset.UnixEpoch, IsNew = true }).ToList();
        await session.SaveManyAsync(events);
        events[1].SetKind("bumped");
        await session.SaveAsync(events[1]); // version 1 on the row
        var copies = events.Select(e => new Event { Id = e.Id, OrderId = orderId, Kind = e.Kind, At = e.At, Version = 0 }).ToList();
        foreach (var copy in copies)
            copy.SetKind("batch");
        var conflict = await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.SaveManyAsync(copies));
        Assert.True(dialect == SqlDialect.Sqlite ? conflict.Key is Guid key && key == events[1].Id : conflict.Message.Contains("wrote 2 of 3 rows", StringComparison.Ordinal), conflict.Message);
        Assert.Equal(["e0", "bumped", "e2"], (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(ef.Events.Where(e => e.OrderId == orderId))).OrderBy(e => e.Kind == "bumped" ? 1 : e.Kind == "e0" ? 0 : 2).Select(e => e.Kind));
    }
}
