using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     Writes are explicit, no tracking: the entity's generated writer holds the INSERT/UPDATE/UPSERT/DELETE per dialect, prepared once per
///     session. A [Version] column is checked on every update and advanced on the object; the database can assign the key ([Identity]);
///     a composite key ([Key] on several properties) matches on all its columns; a chain can update or delete rows in one statement.
/// </summary>
internal static class Writes
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "writes");
        var at = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var orderId = Guid.NewGuid();

        // Insert one row, then a batch: InsertManyAsync writes multi-row INSERTs in lots. From BulkThreshold rows on (1000), a session with the
        // provider's copier (COPY on PostgreSQL, SqlBulkCopy on SQL Server, set in Program.cs; none on SQLite) loads them in bulk instead.
        var created = new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "created", At = at };
        await session.InsertAsync(created);
        var inserted = await session.InsertManyAsync(Enumerable.Range(0, 300).Select(i => new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "batch", At = at }).ToList());
        var bulkOrder = Guid.NewGuid(); // its own order: the checks below count the first order's rows
        var bulk = await session.InsertManyAsync(Enumerable.Range(0, 1200).Select(i => new Event { Id = Guid.NewGuid(), OrderId = bulkOrder, Kind = "bulk", At = at.AddSeconds(i) }).ToList());
        checks.Check("insert and batch insert" + (session.BulkCopier is null ? "" : " (bulk from 1000 rows)"), inserted == 300 && bulk == 1200 && await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId) == 301
            && await session.Query<Event>().CountAsync((e, a) => e.OrderId == a.id && e.Kind == "bulk" && e.At > a.at, (id: bulkOrder, at)) == 1199);

        // Update with optimistic concurrency: WHERE Version = @v, the object's version advances; a stale copy is refused with SqlConcurrencyException.
        created.Kind = "paid";
        await session.UpdateAsync(created);
        var refused = false;
        try
        {
            await session.UpdateAsync(new Event { Id = created.Id, OrderId = orderId, Kind = "stale", At = at, Version = 0 });
        }
        catch (SqlConcurrencyException)
        {
            refused = true;
        }

        checks.Check("update with [Version]", created.Version == 1 && refused && (await session.Query<Event>().FirstOrDefaultAsync((e, id) => e.Id == id, created.Id))?.Kind == "paid");

        // SaveAsync over the entity's own change tracking (IChangeTracking, the repository's contract): inserted while IsNew, then an UPDATE of the
        // modified columns only (SetKind records the change; the version checked and advanced), nothing when nothing changed; the tracking reset after.
        var tracked = new Event { Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Kind = "tracked", At = at, IsNew = true }; // its own order: the counts below are the sample order's
        var savedNew = await session.SaveAsync(tracked);
        tracked.SetKind("saved");
        var savedModified = await session.SaveAsync(tracked);
        var savedUntouched = await session.SaveAsync(tracked);
        var statement = SqlEntityMap<Event>.Current.Writer!.SaveSql(session.Dialect, new HashSet<string> { "Kind" });
        var partial = statement.Contains("SET \"Kind\" = @p0, \"Version\" = \"Version\" + 1 WHERE", StringComparison.Ordinal) || statement.Contains("SET [Kind] = @p0, [Version] = [Version] + 1 WHERE", StringComparison.Ordinal);
        checks.Check("SaveAsync: IsNew, modified columns only, version, reset", savedNew && savedModified && !savedUntouched && !tracked.IsNew && tracked.Version == 1 && tracked.ModifiedProperties.Count == 0 && partial);

        // A graph in one command: the root's modified columns, then for each collection it says changed (CollectionsModified) the lines no longer in the list
        // deleted by key, the new ones inserted, the modified ones updated; one transaction, one round trip, the version checked row by row. A write batch
        // (session.WriteBatch()) is the same mechanism by hand; DeleteGraphAsync cascades from the model (lines, then the invoice).
        var invoice = new Invoice { Id = Guid.NewGuid(), Number = "INV-1", Total = 30m, IsNew = true };
        for (var i = 0; i < 3; i++)
            invoice.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S" + i, Quantity = i + 1, IsNew = true });
        var createdStatements = await session.SaveGraphAsync(invoice); // the invoice and three lines: 4 inserts
        invoice.SetNumber("INV-2");
        invoice.Lines[0].SetQuantity(10);
        invoice.RemoveLine(invoice.Lines[2]);
        invoice.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S3", Quantity = 4, IsNew = true });
        var changedStatements = await session.SaveGraphAsync(invoice); // the root, the orphan delete, one update, one insert
        var lines = await session.Query<InvoiceLine>().Where((l, id) => l.InvoiceId == id, invoice.Id).OrderBy(l => l.Sku).Select(l => new { l.Sku, l.Quantity }).ToListAsync();
        await session.DeleteGraphAsync(invoice);
        var left = await session.Query<InvoiceLine>().CountAsync((l, id) => l.InvoiceId == id, invoice.Id) + await session.Query<Invoice>().CountAsync((i, id) => i.Id == id, invoice.Id);
        checks.Check("SaveGraphAsync: orphans, inserts, updates in one command; DeleteGraphAsync cascades", createdStatements == 4 && changedStatements == 4 && invoice.Version == 1
            && lines.Select(l => (l.Sku, l.Quantity)).SequenceEqual([("S0", 10), ("S1", 2), ("S3", 4)]) && left == 0);

        // Upsert: ON CONFLICT DO UPDATE (SQLite, PostgreSQL), MERGE (SQL Server); inserts when the key is new, updates at the object's version otherwise.
        var draft = new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "draft", At = at };
        await session.UpsertAsync(draft);
        draft.Kind = "final";
        await session.UpsertAsync(draft);
        checks.Check("upsert", draft.Version == 1 && (await session.Query<Event>().FirstOrDefaultAsync((e, id) => e.Id == id, draft.Id)) is { Kind: "final", Version: 1 });

        // Bulk update and delete over a filtered chain: SET values are parameters, an expression over the row is translated; the rows affected come back.
        var newKind = "touched";
        var touched = await session.Query<Event>().Where((e, id) => e.OrderId == id && e.Kind == "batch", orderId).ExecuteUpdateAsync((s, newKind) => s.SetProperty(e => e.Kind, newKind).SetProperty(e => e.Version, e => e.Version + 1), newKind);
        var purged = await session.Query<Event>().Where((e, id) => e.OrderId == id && e.Kind == "touched", orderId).ExecuteDeleteAsync();
        checks.Check("bulk update and delete", touched == 300 && purged == 300 && await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId) == 2);

        // Delete one row; a transaction every command joins until commit or dispose (here rolled back: the ghost never lands).
        await session.DeleteAsync(draft);
        using (var transaction = session.BeginTransaction())
        {
            await session.InsertAsync(new Event { Id = Guid.NewGuid(), OrderId = orderId, Kind = "ghost", At = at });
            transaction.Rollback();
        }

        checks.Check("delete and rollback", await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId) == 1);

        // A key the database assigns: the INSERT leaves the column out and reads it back (RETURNING / OUTPUT INSERTED / last_insert_rowid), in batches too.
        var noteOrder = Guid.NewGuid();
        var note = new Note { OrderId = noteOrder, Text = "first" };
        await session.InsertAsync(note);
        var notes = Enumerable.Range(0, 5).Select(i => new Note { OrderId = noteOrder, Text = "n" + i }).ToList();
        await session.InsertManyAsync(notes);
        checks.Check("[Identity] key read back", note.Id > 0 && notes.Select(n => n.Id).Distinct().Count() == 5 && notes.All(n => n.Id != note.Id));

        // A composite key: update, upsert and delete match on both columns; [Version] works there too.
        var tagged = Guid.NewGuid();
        var urgent = new OrderTag { OrderId = tagged, Tag = "urgent", Note = "a" };
        await session.InsertManyAsync([urgent, new OrderTag { OrderId = tagged, Tag = "gift", Note = "b" }, new OrderTag { OrderId = Guid.NewGuid(), Tag = "urgent", Note = "c" }]);
        urgent.Note = "a2";
        await session.UpdateAsync(urgent);
        await session.UpsertAsync(new OrderTag { OrderId = tagged, Tag = "fragile", Note = "d" });
        var tags = await session.Query<OrderTag>().Where((t, id) => t.OrderId == id, tagged).OrderBy(t => t.Tag).ToListAsync();
        await session.DeleteAsync(urgent);
        checks.Check("composite key writes", urgent.Version == 1 && tags.Count == 3 && tags.Single(t => t.Tag == "urgent").Note == "a2" && await session.Query<OrderTag>().CountAsync(t => t.Tag == "urgent") == 1);
        _ = seed;
        return checks.Failures;
    }
}
