using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Writes of a graph in one command: the write batch (every statement reporting its count, the version checked row by row, an
///     identity key read back), SaveGraphAsync over the root's modified collections (orphans deleted by key, new lines inserted,
///     modified ones updated), DeleteGraphAsync cascading from the model; the final state the one EF Core leaves after the same
///     mutation.
/// </summary>
[Collection("databases")]
public class GraphTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    private static Invoice NewInvoice(string number, params int[] quantities)
    {
        var invoice = new Invoice { Id = Guid.NewGuid(), Number = number, Total = 10m * quantities.Length, IsNew = true };
        for (var i = 0; i < quantities.Length; i++)
            invoice.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S" + i, Quantity = quantities[i], IsNew = true });
        return invoice;
    }

    [Fact]
    public void A_write_batch_joins_the_statements_with_their_counts()
    {
        var invoice = NewInvoice("A", 1, 2);
        invoice.IsNew = false;
        invoice.ResetModifiedProperties();
        invoice.SetNumber("B");
        var line = invoice.Lines[0];
        line.IsNew = false;
        line.SetQuantity(5);
        var postgres = Session(SqlDialect.PostgreSql).WriteBatch().Save(invoice).Save(line).Insert(invoice.Lines[1]).Delete(line);
        Assert.Equal(
            "UPDATE \"Invoices\" SET \"Number\" = @p0, \"Version\" = \"Version\" + 1 WHERE \"Id\" = @p1 AND \"Version\" = @p2 RETURNING 1; "
            + "UPDATE \"InvoiceLines\" SET \"Quantity\" = @p3 WHERE \"Id\" = @p4 RETURNING 1; "
            + "INSERT INTO \"InvoiceLines\" (\"Id\", \"InvoiceId\", \"Sku\", \"Quantity\") VALUES (@p5, @p6, @p7, @p8) RETURNING 1; "
            + "DELETE FROM \"InvoiceLines\" WHERE \"Id\" = @p9 RETURNING 1",
            postgres.ToSql());
        var sqlServer = Session(SqlDialect.SqlServer).WriteBatch().Save(invoice).Insert(new Note { OrderId = Guid.NewGuid(), Text = "n" });
        Assert.Equal(
            "UPDATE [Invoices] SET [Number] = @p0, [Version] = [Version] + 1 WHERE [Id] = @p1 AND [Version] = @p2; SELECT @@ROWCOUNT; "
            + "INSERT INTO [Notes] ([OrderId], [Text]) OUTPUT INSERTED.[Id] VALUES (@p3, @p4)",
            sqlServer.ToSql()); // an identity insert reports its own rows
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_graph_saves_in_one_command_and_ends_as_ef_core_leaves_it(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var log = new List<string>();
        using var ef = databases.Ef(dialect, log.Add);

        // A new root with new lines: inserts only (no orphan delete under a new parent).
        var ours = NewInvoice("INV-1", 1, 2, 3, 4);
        Assert.Equal(5, await session.SaveGraphAsync(ours));
        Assert.False(ours.IsNew);
        Assert.All(ours.Lines, l => Assert.False(l.IsNew));
        Assert.Empty(ours.CollectionsModified);
        var theirs = new Invoice { Id = Guid.NewGuid(), Number = "INV-1", Total = 40m };
        for (var i = 0; i < 4; i++)
            theirs.Lines.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = theirs.Id, Sku = "S" + i, Quantity = i + 1 });
        ef.Invoices.Add(theirs);
        await ef.SaveChangesAsync();
        ef.ChangeTracker.Clear();

        // The same mutation on both: the number, three lines modified, two added, one removed. Ours: one command of 1 + 1 + 3 + 2 statements.
        ours.SetNumber("INV-2");
        for (var i = 0; i < 3; i++)
            ours.Lines[i].SetQuantity(10 + i);
        ours.RemoveLine(ours.Lines[3]);
        ours.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S4", Quantity = 40, IsNew = true });
        ours.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S5", Quantity = 50, IsNew = true });
        Assert.Equal(7, await session.SaveGraphAsync(ours));
        Assert.Equal(1, ours.Version);
        Assert.Empty(ours.ModifiedProperties);
        Assert.Empty(ours.CollectionsModified);
        Assert.All(ours.Lines, l => Assert.Empty(l.ModifiedProperties));

        // The oracle's context does not track by default: this query must, for SaveChanges to see the mutation.
        var attached = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsTracking(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(ef.Invoices, i => i.Lines)), i => i.Id == theirs.Id);
        Assert.Equal((0, 4), (attached.Version, attached.Lines.Count)); // the reference invoice is as EF Core wrote it: ours touched nothing of it
        attached.Number = "INV-2";
        var byOrder = attached.Lines.OrderBy(l => l.Sku).ToList();
        for (var i = 0; i < 3; i++)
            byOrder[i].Quantity = 10 + i;
        ef.Remove(byOrder[3]);
        // A new line with a key the application sets goes through Add: found through the collection alone, EF Core would take it as modified (an UPDATE of nothing).
        ef.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = attached.Id, Sku = "S4", Quantity = 40 });
        ef.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = attached.Id, Sku = "S5", Quantity = 50 });
        try
        {
            await ef.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException e)
        {
            Assert.Fail($"EF Core's own save failed: {e.Message}\n{string.Join("\n", log)}");
        }

        ef.ChangeTracker.Clear();

        // The final state, read with EF Core on both: the same number, the same lines.
        var mine = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(ef.Invoices, i => i.Lines), i => i.Id == ours.Id);
        var ref1 = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(ef.Invoices, i => i.Lines), i => i.Id == theirs.Id);
        Assert.Equal(ref1.Number, mine.Number);
        Assert.Equal(ref1.Lines.OrderBy(l => l.Sku).Select(l => (l.Sku, l.Quantity)), mine.Lines.OrderBy(l => l.Sku).Select(l => (l.Sku, l.Quantity)));
        Assert.Equal([("S0", 10), ("S1", 11), ("S2", 12), ("S4", 40), ("S5", 50)], mine.Lines.OrderBy(l => l.Sku).Select(l => (l.Sku, l.Quantity)));

        // A stale root: the version check names it, and the transaction leaves the lines as they were.
        var stale = new Invoice { Id = ours.Id, Number = "INV-2", Total = ours.Total, Version = 0 };
        stale.SetNumber("INV-3");
        stale.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "S6", Quantity = 60, IsNew = true });
        var conflict = await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.SaveGraphAsync(stale));
        Assert.Equal(ours.Id, conflict.Key);
        ef.ChangeTracker.Clear();
        Assert.Equal(5, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Set<InvoiceLine>(), l => l.InvoiceId == ours.Id));
        Assert.Equal("INV-2", (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(ef.Invoices, i => i.Id == ours.Id)).Number);

        // An untouched graph: nothing to run. A root with its collection cleared: every line gone.
        Assert.Equal(0, await session.SaveGraphAsync(ours));
        foreach (var line in ours.Lines.ToList())
            ours.RemoveLine(line);
        Assert.Equal(1, await session.SaveGraphAsync(ours));
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Set<InvoiceLine>(), l => l.InvoiceId == ours.Id));

        // The cascade from the model: lines, then the root; an identity row in a batch comes back with its key.
        await session.DeleteGraphAsync(ref1);
        ef.ChangeTracker.Clear();
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Set<InvoiceLine>(), l => l.InvoiceId == ref1.Id));
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(ef.Invoices, i => i.Id == ref1.Id));
        var note = new Note { OrderId = Guid.NewGuid(), Text = "batched" };
        var other = new Note { OrderId = note.OrderId, Text = "too" };
        await session.WriteBatch().Insert(note).Insert(other).ExecuteAsync();
        Assert.True(note.Id > 0 && other.Id > note.Id);
    }
}
