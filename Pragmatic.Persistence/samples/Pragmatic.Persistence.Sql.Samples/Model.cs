using System.Linq.Expressions;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>The Orders table seen through a visibility rule: a deleted order is not there for any read, unless the chain says WithoutFilters().</summary>
[Entity]
[Table("Orders")]
[VisibleWhen<NotDeleted>]
public sealed class LiveOrder : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Number { get; set; } = "";
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>A rule is one predicate on the entity, as a lambda the generator reads; it is translated at compile time and appended to every plan.</summary>
public sealed class NotDeleted : VisibilityRule<LiveOrder>
{
    public override Expression<Func<LiveOrder, bool>> ToExpression() => order => !order.IsDeleted;
}

/// <summary>
///     The model beyond plain columns, on the entities of the workload's Extras (their tables created by ExtrasSchema): every column
///     type, converters, an enum stored as its name, an owned type flattened into the owner's table, a hierarchy in one table, a tree
///     walked with a recursive CTE, a JSON column through a source-generated context, and visibility rules.
/// </summary>
internal static class Model
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "model");

        // Column types beyond the usual (DateTime, byte[], DateOnly, TimeOnly, TimeSpan as ticks), and converters: [Convert<SqlUtcTicks>] stores a
        // DateTimeOffset as UTC ticks, [Convert<SqlEnumName<T>>] an enum as its name; predicates compare through the converter, projections read through it.
        var stamp = new DateTimeOffset(2026, 10, 19, 21, 30, 15, TimeSpan.FromHours(2));
        var typed = new TypedRow { Id = Guid.NewGuid(), When = new DateTime(2026, 10, 19, 21, 30, 15, 123), Blob = [1, 2, 3], Day = new DateOnly(2026, 10, 19), Clock = new TimeOnly(21, 30, 15), Span = TimeSpan.FromMinutes(90), Stamp = stamp, Status = OrderStatus.Paid };
        await session.InsertAsync(typed);
        var back = await session.Query<TypedRow>().FirstOrDefaultAsync((r, id) => r.Id == id, typed.Id);
        var dto = await session.Query<TypedRow>().Where((r, id) => r.Id == id, typed.Id).Project<TypedStampDto>().FirstOrDefaultAsync();
        checks.Check("column types and converters", back is not null && back.When == typed.When && back.Blob.SequenceEqual(typed.Blob) && back.Day == typed.Day && back.Clock == typed.Clock && back.Span == typed.Span && back.Stamp == stamp && back.Status == OrderStatus.Paid
            && await session.Query<TypedRow>().CountAsync((r, since) => r.Stamp > since, stamp.AddHours(-1)) == 1 && await session.Query<TypedRow>().CountAsync(r => r.Status == OrderStatus.Paid) == 1 && dto?.Stamp == stamp);

        // An [Owned] type: its members are columns {Property}_{Member} of the owner's table, built together when the row is read, null together when optional.
        var shipment = new Shipment { Id = Guid.NewGuid(), Code = "S-1", Destination = new Address { Street = "Via Roma 1", City = "Rome", Floor = 3 } };
        await session.InsertAsync(shipment);
        var inRome = await session.Query<Shipment>().CountAsync(s => s.Destination.City == "Rome");
        var row = await session.Query<Shipment>().Where((s, id) => s.Id == id, shipment.Id).Project<ShipmentRowDto>().FirstOrDefaultAsync();
        checks.Check("owned type", inRome == 1 && row is { Street: "Via Roma 1", DestinationCity: "Rome" } && (await session.Query<Shipment>().FirstOrDefaultAsync((s, id) => s.Id == id, shipment.Id))?.Return is null);

        // A hierarchy in one table ([Inheritance] on the root, derived [Entity] types): the root reads each row as its own type (a switch on the
        // discriminator), a derived type reads its rows only (a fixed predicate on the discriminator in every plan).
        await session.InsertAsync<Charge>(new CardCharge { Id = Guid.NewGuid(), Amount = 10m, Description = "card", Last4 = "1234" });
        await session.InsertAsync(new WireCharge { Id = Guid.NewGuid(), Amount = 20m, Description = "wire", Iban = "IT00X", Days = 2 });
        var charges = await session.Query<Charge>().OrderBy(c => c.Amount).ToListAsync();
        checks.Check("hierarchy in one table", charges.Count == 2 && charges[0] is CardCharge { Last4: "1234" } && charges[1] is WireCharge { Days: 2 } && await session.Query<WireCharge>().CountAsync() == 1);

        // The hierarchy inside a lambda: `is` tests the discriminator, a cast reads the derived type's own columns, `?:` as a predicate is its boolean
        // form (no CASE in the WHERE), OfType narrows the chain to one derived type (the same statement as Query<WireCharge>()).
        var wires = await session.Query<Charge>().Where(c => c is WireCharge && ((WireCharge)c).Days > 1).ToListAsync();
        var cards = await session.Query<Charge>().OfType<CardCharge>().ToListAsync();
        var either = await session.Query<Charge>().CountAsync(c => c is CardCharge ? ((CardCharge)c).Last4 == "1234" : ((WireCharge)c).Iban == "IT00X");
        checks.Check("type tests, casts, OfType, conditional as a predicate", wires.Count == 1 && cards.Count == 1 && cards[0].Last4 == "1234" && either == 2);

        // A polymorphic parent: the root names it (ParentType + ParentId), each derived type declares its own Parent on that key. The filter walks to the
        // parent only on the matching branch: a LEFT JOIN per derived navigation (the row may be of another type), the discriminator guarding each arm.
        var customer = seed.Customers[0];
        var order = seed.Orders[0];
        var toCustomer = new CustomerAssignment { Id = Guid.NewGuid(), ParentId = customer.Id, ParentType = ParentKind.Customer, Note = "customer", Role = "owner" };
        var toOrder = new OrderAssignment { Id = Guid.NewGuid(), ParentId = order.Id, ParentType = ParentKind.Order, Note = "order" };
        var toSite = new SiteAssignment { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), ParentType = ParentKind.Site, Note = "site", Floor = 2 };
        await session.InsertAsync<Assignment>(toCustomer);
        await session.InsertAsync<Assignment>(toOrder);
        await session.InsertAsync<Assignment>(toSite);
        await session.InsertManyAsync([new Reminder { Id = Guid.NewGuid(), AssignmentId = toCustomer.Id, Text = "c" }, new Reminder { Id = Guid.NewGuid(), AssignmentId = toOrder.Id, Text = "o" }, new Reminder { Id = Guid.NewGuid(), AssignmentId = toSite.Id, Text = "s" }]);
        var visible = await session.Query<Reminder>().Where((r, ids) =>
            r.Assignment is CustomerAssignment ? ids.Contains(((CustomerAssignment)r.Assignment).Parent.Id)
            : r.Assignment is OrderAssignment ? ids.Contains(((OrderAssignment)r.Assignment).Parent.CustomerId)
            : r.Assignment.ParentType == ParentKind.Site ? ids.Contains(r.Assignment.ParentId)
            : true, new[] { customer.Id, toSite.ParentId }).OrderBy(r => r.Text).Select(r => r.Text).ToListAsync();
        checks.Check("polymorphic parent: ParentType + ParentId, each derived type's Parent", visible.SequenceEqual(order.CustomerId == customer.Id ? ["c", "o", "s"] : ["c", "s"]));

        // A hierarchy with a table per concrete type ([Inheritance(Strategy = Tpc)]): each derived type its own table, the root a view over them that the
        // generator writes (PaymentView.CreateSql, run by the schema); reads through the root come back typed, a write through the root goes to the row's table.
        var paidOrder = seed.Orders[0];
        var card = new CardPayment { Id = Guid.NewGuid(), OrderId = paidOrder.Id, Amount = 10m, PaidAt = new DateTime(2026, 10, 20, 9, 0, 0), Last4 = "1234", Brand = "visa" };
        var wire = new WirePayment { Id = Guid.NewGuid(), OrderId = paidOrder.Id, Amount = 20m, PaidAt = new DateTime(2026, 10, 20, 9, 0, 0), Iban = "IT00X", Days = 2 };
        await session.InsertManyAsync<Payment>([card, wire]);
        var payments = await session.Query<Payment>().Where((p, id) => p.OrderId == id, paidOrder.Id).OrderBy(p => p.Amount).ToListAsync();
        var wirePayments = await session.Query<Payment>().CountAsync(p => p is WirePayment);
        await session.DeleteAsync<Payment>(wire);
        checks.Check("table per concrete type: view root, typed reads, dispatched writes", payments.Count == 2 && payments[0] is CardPayment { Brand: "visa" } && payments[1] is WirePayment { Days: 2 } && wirePayments == 1 && await session.Query<WirePayment>().CountAsync() == 0 && PaymentView.SelectSql(session.Dialect).Contains("UNION ALL"));

        // A many-to-many side ([ManyToMany("ProjectLabels", "ProjectId", "LabelId")]): links written one at a time (LinkAsync) or synced by a graph save from
        // the tracked list (the list is the truth: a link to a label not in it goes); the collection in lambdas and in Include goes through the join table.
        var urgent = new Label { Id = Guid.NewGuid(), Name = "urgent" };
        var billing = new Label { Id = Guid.NewGuid(), Name = "billing" };
        var project = new Project { Id = Guid.NewGuid(), Name = "alpha", IsNew = true };
        await session.InsertManyAsync([urgent, billing]);
        await session.SaveAsync(project);
        await session.LinkAsync(project, nameof(Project.Labels), urgent);
        project.Labels.Add(urgent);
        project.AddLabel(billing);
        await session.SaveGraphAsync(project); // the urgent link kept, the billing one inserted
        var labelled = await session.Query<Project>().Where((p, id) => p.Id == id, project.Id).Include(p => p.Labels).FirstOrDefaultAsync();
        var withBilling = await session.Query<Project>().CountAsync((p, id) => p.Id == id && p.Labels.Any(l => l.Name == "billing"), project.Id);
        await session.UnlinkAsync(project, nameof(Project.Labels), urgent);
        var left = await session.Query<Project>().Where((p, id) => p.Id == id, project.Id).Select(p => p.Labels.Count).FirstOrDefaultAsync();
        checks.Check("many-to-many: links, Include and predicates through the join table", labelled is { Labels.Count: 2 } && withBilling == 1 && left == 1);

        // A sequence (session.NextValueAsync / NextValuesAsync): PostgreSQL and SQL Server sequences, a counter table on SQLite; DDL from SqlSequences.CreateSql.
        var number = await session.NextValueAsync(ExtrasSchema.TicketNumbers);
        var numbers = await session.NextValuesAsync(ExtrasSchema.TicketNumbers, 3);
        checks.Check("sequence: next value and a range", number >= 1000 && numbers.Length == 3 && numbers[0] > number && numbers[2] > numbers[1]);

        // A tree: [GenerateHierarchy] on an entity with a navigation to itself emits GetDescendantsByParent / GetAncestorsByParent, each one recursive CTE.
        var root = new Category { Id = Guid.NewGuid(), Name = "root" };
        var child = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = "child" };
        var grandchild = new Category { Id = Guid.NewGuid(), ParentId = child.Id, Name = "grandchild" };
        await session.InsertManyAsync([root, child, grandchild]);
        checks.Check("tree walks (recursive CTE)", (await session.GetDescendantsByParent(root.Id)).Count == 2 && (await session.GetAncestorsByParent(grandchild.Id)).Count == 2);

        // The same walk by hand: session.With(anchor, (c, tree) => step) is a recursive CTE the chain goes on from (a filter with its own value, an ordering, a
        // count); session.With(query, materialized: true) a plain one, computed once where the dialect has MATERIALIZED.
        var walked = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), (c, tree) => c.ParentId == tree.Id).Where((c, id) => c.Id != id, root.Id).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        var up = await session.With(session.Query<Category>().Where((c, id) => c.Id == id, grandchild.Id), (c, tree) => c.Id == tree.ParentId).CountAsync((c, id) => c.Id != id, grandchild.Id);
        var once = await session.With(session.Query<Category>().Where((c, id) => c.ParentId == id, root.Id), materialized: true).CountAsync();
        checks.Check("general CTE: With(anchor, step) walks the tree, With(query, materialized) computes once", walked.SequenceEqual(["child", "grandchild"]) && up == 2 && once == 1);

        // A JSON column: [Convert<SqlJson<T, TContext>>] with a System.Text.Json source-generated context (no reflection), jsonb on PostgreSQL through [Column(Cast = "jsonb")].
        var document = new Document { Id = Guid.NewGuid(), Name = "manual", Spec = new DocumentSpec { Title = "Manual", Tags = ["pdf"], Pages = 42 } };
        await session.InsertAsync(document);
        document.Spec.Pages = 43;
        await session.UpdateAsync(document);
        var documentBack = await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, document.Id);
        checks.Check("JSON column", documentBack is not null && documentBack.Spec.Title == "Manual" && documentBack.Spec.Pages == 43 && documentBack.Spec.Tags.SequenceEqual(["pdf"]));

        // Into the document: a member path is the value at the JSON path (->> with a cast, json_extract, JSON_VALUE), in predicates, projections and orderings;
        // an array's Contains and Count; a member set in place with an update (jsonb_set / json_set / JSON_MODIFY), the rest of the document untouched.
        var heavy = await session.Query<Document>().CountAsync((d, a) => d.Id == a.id && d.Spec.Pages > a.pages && d.Spec.Tags.Contains(a.tag), (id: document.Id, pages: 40, tag: "pdf"));
        var shapedJson = await session.Query<Document>().Where((d, id) => d.Id == id, document.Id).Select(d => new { d.Spec.Title, Tags = d.Spec.Tags.Count }).FirstOrDefaultAsync();
        await session.Query<Document>().Where((d, id) => d.Id == id, document.Id).ExecuteUpdateAsync((s, pages) => s.SetProperty(d => d.Spec.Pages, pages), 44);
        var patched = await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, document.Id);
        checks.Check("JSON paths: predicate, projection, array Contains/Count, member update", heavy == 1 && shapedJson is { Title: "Manual", Tags: 1 } && patched is { Spec: { Pages: 44, Title: "Manual" } });

        // A range of days (SqlDateRange: an owned type, two bound columns, either end open): Contains is daterange(...) @> on PostgreSQL, the two bounds compared
        // elsewhere (PRAG3012 says so). A list in a predicate is an array parameter on PostgreSQL (= ANY, the text constant) and an IN expanded per call elsewhere.
        var ranged = new Invoice { Id = Guid.NewGuid(), Number = "RANGE", Total = 0m, Active = SqlDateRange.Between(new DateOnly(2026, 1, 1), null), IsNew = true };
        await session.SaveAsync(ranged);
        var inForce = await session.Query<Invoice>().CountAsync((i, a) => i.Number == a.number && i.Active.Contains(a.day), (number: "RANGE", day: new DateOnly(2026, 10, 19)));
        var before = await session.Query<Invoice>().CountAsync((i, a) => i.Number == a.number && i.Active.Contains(a.day), (number: "RANGE", day: new DateOnly(2025, 12, 31)));
        var someIds = seed.Orders.Take(3).Select(o => o.Id).ToList();
        var inList = await session.Query<Order>().CountAsync((o, ids) => ids.Contains(o.Id), someIds);
        checks.Check("date range (Contains) and a list as an array on PostgreSQL", inForce == 1 && before == 0 && inList == 3);

        // A view ([Table(ReadOnly = true)]): read, filtered and paged like a table; never written (no writer, a command through a chain is PRAG3001). A table-valued
        // function as the root (session.Function<T>("name", args): FROM name(@p0) t; dbo. on SQL Server), the chain going on with navigations and joins.
        var busyOnes = await session.Query<CustomerSummary>().Where((s, atLeast) => s.Orders >= atLeast, 20).OrderByDescending(s => s.Orders).ThenBy(s => s.Name).Take(3).Select(s => s.Name).ToListAsync();
        var expectedBusy = seed.Customers.Select(c => (c.Name, Orders: seed.Orders.Count(o => o.CustomerId == c.Id))).Where(c => c.Orders >= 20).OrderByDescending(c => c.Orders).ThenBy(c => c.Name, StringComparer.Ordinal).Take(3).Select(c => c.Name);
        var viewWriteRefused = false;
        try
        {
            await session.InsertAsync(new CustomerSummary { Id = Guid.NewGuid(), Name = "never" });
        }
        catch (InvalidOperationException e) when (e.Message.Contains("read-only", StringComparison.Ordinal))
        {
            viewWriteRefused = true;
        }

        var viaFunction = session.Dialect is SqlDialect.Sqlite or SqlDialect.MySql ? seed.Orders.Count(o => o.CustomerId == seed.Customers[0].Id) // SQLite and MySQL have no table-valued functions: the chain renders (SQLite), the database has none
            : await session.Function<Order>("orders_of", seed.Customers[0].Id).Where(o => o.Customer!.Id == o.CustomerId).CountAsync();
        checks.Check("a read-only view, a table-valued function as the root", busyOnes.SequenceEqual(expectedBusy) && viewWriteRefused && viaFunction == seed.Orders.Count(o => o.CustomerId == seed.Customers[0].Id));

        // A visibility rule: every plan on LiveOrder carries NOT IsDeleted, unless the chain reads past it with WithoutFilters().
        var live = await session.Query<LiveOrder>().CountAsync();
        var every = await session.Query<LiveOrder>().WithoutFilters().CountAsync();
        checks.Check("visibility rule and WithoutFilters", live == seed.Orders.Count(o => !o.IsDeleted) && every == seed.Orders.Count);
        return checks.Failures;
    }
}
