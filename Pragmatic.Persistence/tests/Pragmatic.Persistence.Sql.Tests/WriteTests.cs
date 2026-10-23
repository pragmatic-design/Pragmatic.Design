using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The writes, read back with EF Core on the same database: insert, batch insert, update with the version
///     advancing, a stale update refused, delete, and a transaction rolled back. Each test writes events of its
///     own order id, so the seed and the other tests stay untouched.
/// </summary>
[Collection("databases")]
public partial class WriteTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static Event NewEvent(Guid orderId, string kind) => new() { Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, At = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero) };
}

internal static class LetExtensions
{
    /// <summary>Projects one object inline, so an assertion can compare a few of its members at once.</summary>
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> project) => project(value);
}

public partial class WriteTests
{

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Insert_update_and_delete_are_what_ef_core_reads_back(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();

        var created = NewEvent(orderId, "created");
        await session.InsertAsync(created);
        var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(ef.Events, e => e.Id == created.Id);
        Assert.NotNull(stored);
        Assert.Equal(("created", 0, created.At), (stored.Kind, stored.Version, stored.At));

        created.Kind = "paid";
        await session.UpdateAsync(created);
        Assert.Equal(1, created.Version); // the object advances with the row
        ef.ChangeTracker.Clear();
        var updated = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Events, e => e.Id == created.Id);
        Assert.Equal(("paid", 1), (updated.Kind, updated.Version));

        // Someone else's copy, still at version 0: refused, nothing written.
        var stale = new Event { Id = created.Id, OrderId = orderId, Kind = "cancelled", At = created.At, Version = 0 };
        var conflict = await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.UpdateAsync(stale));
        Assert.Equal(0, conflict.Version);
        ef.ChangeTracker.Clear();
        Assert.Equal("paid", (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Events, e => e.Id == created.Id)).Kind);

        await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.DeleteAsync(stale));
        await session.DeleteAsync(created);
        ef.ChangeTracker.Clear();
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.Id == created.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_batch_insert_writes_every_row_and_a_rolled_back_transaction_none(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();

        var rows = Enumerable.Range(0, 1000).Select(i => NewEvent(orderId, "e" + i)).ToList();
        var written = await session.InsertManyAsync(rows);
        Assert.Equal(1000, written);
        Assert.Equal(1000, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.OrderId == orderId));
        Assert.Equal(1000, await session.Query<Event>().CountAsync((e, orderId) => e.OrderId == orderId, orderId));

        var rolledBack = Guid.NewGuid();
        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(NewEvent(rolledBack, "ghost"));
            Assert.Equal(1, await session.Query<Event>().CountAsync((e, rolledBack) => e.OrderId == rolledBack, rolledBack)); // visible inside the transaction
            transaction.Rollback();
        }

        Assert.Equal(0, await session.Query<Event>().CountAsync((e, rolledBack) => e.OrderId == rolledBack, rolledBack));
        Assert.Null(session.Transaction);

        var committed = Guid.NewGuid();
        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(NewEvent(committed, "kept"));
            transaction.Commit();
        }

        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.OrderId == committed));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task An_identity_key_comes_back_into_the_object_one_row_or_a_batch(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();

        var note = new Note { OrderId = orderId, Text = "first" };
        await session.InsertAsync(note);
        Assert.True(note.Id > 0);
        var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(ef.Notes, n => n.Id == note.Id);
        Assert.Equal("first", stored.Text);

        // 300 rows: two statements (256 + 44); every object gets the key of its own row (RETURNING / OUTPUT in VALUES order).
        var rows = Enumerable.Range(0, 300).Select(i => new Note { OrderId = orderId, Text = "n" + i }).ToList();
        Assert.Equal(300, await session.InsertManyAsync(rows));
        Assert.Equal(300, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => Assert.True(r.Id > note.Id));
        ef.ChangeTracker.Clear();
        var byKey = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(ef.Notes.Where(n => n.OrderId == orderId), n => n.Id, n => n.Text);
        Assert.All(rows, r => Assert.Equal(r.Text, byKey[r.Id]));

        note.Text = "edited";
        await session.UpdateAsync(note);
        await session.DeleteAsync(rows[0]);
        ef.ChangeTracker.Clear();
        Assert.Equal("edited", (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(ef.Notes, n => n.Id == note.Id)).Text);
        Assert.Equal(300, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Notes, n => n.OrderId == orderId));
        await Assert.ThrowsAsync<NotSupportedException>(() => session.UpsertAsync(note)); // no client key to match on
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_bulk_update_and_a_bulk_delete_touch_the_filtered_rows_in_one_statement(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();
        await session.InsertManyAsync(Enumerable.Range(0, 3).Select(i => NewEvent(orderId, "created")).ToList());
        await session.InsertAsync(NewEvent(orderId, "other"));

        // The declared form: values bound from the arguments, the version advanced in SQL.
        Assert.Equal(3, await session.Rekind(orderId, "created", "paid"));
        var paid = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(ef.Events.Where(e => e.OrderId == orderId && e.Kind == "paid"));
        Assert.Equal(3, paid.Count);
        Assert.All(paid, e => Assert.Equal(1, e.Version));

        // The inline form: the caller's values as the arguments of the predicate and of the SET list, the literals as constants of the plan.
        var done = "done";
        Assert.Equal(3, await session.Query<Event>().Where((e, orderId) => e.OrderId == orderId && e.Kind == "paid", orderId).ExecuteUpdateAsync((s, done) => s.SetProperty(e => e.Kind, done), done));
        ef.ChangeTracker.Clear();
        Assert.Equal(3, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.OrderId == orderId && e.Kind == "done"));
        Assert.Equal(0, await session.Query<Event>().Where((e, orderId) => e.OrderId == orderId && e.Kind == "nope", orderId).ExecuteDeleteAsync());

        Assert.Equal(4, await session.PurgeEvents(orderId));
        ef.ChangeTracker.Clear();
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.OrderId == orderId));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_composite_key_is_matched_on_every_column_by_update_upsert_and_delete(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();
        var otherOrder = Guid.NewGuid();

        var urgent = new OrderTag { OrderId = orderId, Tag = "urgent", Note = "ship first" };
        var gift = new OrderTag { OrderId = orderId, Tag = "gift", Note = "wrap" };
        var sameTagOtherOrder = new OrderTag { OrderId = otherOrder, Tag = "urgent", Note = "other" };
        await session.InsertManyAsync([urgent, gift, sameTagOtherOrder]);

        // Update and delete touch the row with both key columns, not every row sharing one of them.
        urgent.Note = "ship today";
        await session.UpdateAsync(urgent);
        Assert.Equal(1, urgent.Version);
        var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(ef.OrderTags.Where(t => t.Tag == "urgent"));
        Assert.Equal(("ship today", 1), stored.Single(t => t.OrderId == orderId).Let(t => (t.Note, t.Version)));
        Assert.Equal(("other", 0), stored.Single(t => t.OrderId == otherOrder).Let(t => (t.Note, t.Version)));

        var stale = new OrderTag { OrderId = orderId, Tag = "urgent", Note = "late", Version = 0 };
        await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.UpdateAsync(stale));

        gift.Note = "wrap and card";
        await session.UpsertAsync(gift); // there: updated at version 0 → 1
        await session.UpsertAsync(new OrderTag { OrderId = orderId, Tag = "fragile", Note = "glass" }); // not there: inserted
        ef.ChangeTracker.Clear();
        var ofOrder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(ef.OrderTags.Where(t => t.OrderId == orderId));
        Assert.Equal(["fragile", "gift", "urgent"], ofOrder.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(("wrap and card", 1), ofOrder.Single(t => t.Tag == "gift").Let(t => (t.Note, t.Version)));

        await session.DeleteAsync(sameTagOtherOrder);
        ef.ChangeTracker.Clear();
        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.OrderTags, t => t.Tag == "urgent")); // the order's own urgent tag stays
        Assert.Equal(3, await session.Query<OrderTag>().CountAsync((t, orderId) => t.OrderId == orderId, orderId));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task An_upsert_inserts_then_updates_at_the_objects_version(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var orderId = Guid.NewGuid();

        var created = NewEvent(orderId, "created");
        await session.UpsertAsync(created); // not there: inserted, version as given
        Assert.Equal(0, created.Version);
        var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(ef.Events, e => e.Id == created.Id);
        Assert.Equal(("created", 0), (stored.Kind, stored.Version));

        created.Kind = "paid";
        await session.UpsertAsync(created); // there: updated, the version advances in the row and the object
        Assert.Equal(1, created.Version);
        ef.ChangeTracker.Clear();
        var updated = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(ef.Events, e => e.Id == created.Id);
        Assert.Equal(("paid", 1), (updated.Kind, updated.Version));
        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Events, e => e.OrderId == orderId));

        var stale = new Event { Id = created.Id, OrderId = orderId, Kind = "cancelled", At = created.At, Version = 0 };
        var conflict = await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.UpsertAsync(stale));
        Assert.Equal(0, conflict.Version);
        ef.ChangeTracker.Clear();
        Assert.Equal("paid", (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(ef.Events, e => e.Id == created.Id)).Kind);
    }
}
