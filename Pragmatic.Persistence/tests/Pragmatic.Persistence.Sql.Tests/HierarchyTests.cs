using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A hierarchy in one table inside the lambdas: type tests on the discriminator (the row, a navigation of it), casts to a derived
///     type's own columns and navigations (a LEFT JOIN under the type's alias), the conditional as a predicate in its boolean form,
///     <c>OfType</c>, and a polymorphic parent (<c>ParentType</c> + <c>ParentId</c> on the root, each derived type's <c>Parent</c> on that
///     key): the shape of provisus' association-task visibility filter, rewritten and compared with EF Core's result.
/// </summary>
[Collection("databases")]
public class HierarchyTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Type_tests_casts_and_conditionals_render_on_the_discriminator()
    {
        var session = Session(SqlDialect.PostgreSql);

        // The row's type: the derived type's predicate on the discriminator; negated; a cast reads the derived type's own column.
        Assert.EndsWith("WHERE (t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge'))", session.Query<Charge>().Where(c => c is CardCharge).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE (NOT (t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge')))", session.Query<Charge>().Where(c => c is not CardCharge).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE ((NOT (t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge'))) AND (t.\"Days\" > @p0))", session.Query<Charge>().Where(c => !(c is CardCharge) && ((WireCharge)c).Days > 1).ToSql(), StringComparison.Ordinal);

        // OfType: the same statement as the derived type's own chain.
        Assert.Equal(session.Query<WireCharge>().Where(w => w.Days > 0).ToSql(), session.Query<Charge>().OfType<WireCharge>().Where(w => w.Days > 0).ToSql());
        Assert.Equal(session.Query<CardCharge>().Where(c => c.Amount > 0m).OrderBy(c => c.Amount).Select(c => c.Last4).ToSql(), session.Query<Charge>().Where(c => c.Amount > 0m).OfType<CardCharge>().OrderBy(c => c.Amount).Select(c => c.Last4).ToSql());

        // A conditional as a predicate: its boolean form, the literal arms folded; as a value, CASE.
        var conditional = session.Query<Charge>().Where(c => c is CardCharge ? ((CardCharge)c).Last4 == "1234" : ((WireCharge)c).Days > 1).ToSql();
        Assert.EndsWith("WHERE (((t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge')) AND (t.\"Last4\" = @p0)) OR ((NOT (t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge'))) AND (t.\"Days\" > @p1)))", conditional, StringComparison.Ordinal);
        Assert.EndsWith("WHERE ((NOT (t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge'))) OR (t.\"Last4\" = @p0))", session.Query<Charge>().Where(c => c is CardCharge ? ((CardCharge)c).Last4 == "1234" : true).ToSql(), StringComparison.Ordinal);
        Assert.StartsWith("SELECT t.\"Id\", CASE WHEN t.\"Kind\" IN ('CardCharge', 'PremiumCardCharge') THEN 'card' ELSE 'other' END FROM \"Charges\" t", session.Query<Charge>().Select(c => new { c.Id, Kind = c is CardCharge ? "card" : "other" }).ToSql(), StringComparison.Ordinal);

        // The polymorphic parent: the type test on the navigation's row, each derived type's Parent a LEFT JOIN under the type's alias, the root's ParentType through its converter.
        var filter = session.Query<Reminder>().Where((r, ids) =>
            r.Assignment is CustomerAssignment
                ? ((CustomerAssignment)r.Assignment).Parent.Orders.Count == 0 || ((CustomerAssignment)r.Assignment).Parent.Orders.All(o => !o.IsDeleted && ids.Contains(o.Id))
            : r.Assignment is OrderAssignment
                ? ids.Contains(((OrderAssignment)r.Assignment).Parent.CustomerId)
            : r.Assignment.ParentType == ParentKind.Site
                ? ids.Contains(r.Assignment.ParentId)
            : true, new[] { Guid.Empty }).ToSql();
        Assert.True(filter.Contains("INNER JOIN \"Assignments\" j_Assignment ON j_Assignment.\"Id\" = t.\"AssignmentId\"", StringComparison.Ordinal), filter);
        Assert.True(filter.Contains("LEFT JOIN \"Customers\" j_Assignment_CustomerAssignment_Parent ON j_Assignment_CustomerAssignment_Parent.\"Id\" = j_Assignment.\"ParentId\"", StringComparison.Ordinal), filter);
        Assert.True(filter.Contains("LEFT JOIN \"Orders\" j_Assignment_OrderAssignment_Parent ON j_Assignment_OrderAssignment_Parent.\"Id\" = j_Assignment.\"ParentId\"", StringComparison.Ordinal), filter);
        Assert.True(filter.Contains("(j_Assignment.\"Kind\" = 'CustomerAssignment') AND (((SELECT COUNT(*) FROM \"Orders\" c1 WHERE c1.\"CustomerId\" = j_Assignment_CustomerAssignment_Parent.\"Id\") = @p0) OR (NOT EXISTS (SELECT 1 FROM \"Orders\" c1 WHERE c1.\"CustomerId\" = j_Assignment_CustomerAssignment_Parent.\"Id\" AND NOT (", StringComparison.Ordinal), filter);
        Assert.True(filter.Contains("(NOT (j_Assignment.\"Kind\" = 'CustomerAssignment')) AND (((j_Assignment.\"Kind\" = 'OrderAssignment') AND (j_Assignment_OrderAssignment_Parent.\"CustomerId\" = ANY(@p3)", StringComparison.Ordinal), filter); // PostgreSQL: the list as an array
        Assert.True(filter.Contains("(NOT (j_Assignment.\"Kind\" = 'OrderAssignment')) AND ((NOT (j_Assignment.\"ParentType\" = @p", StringComparison.Ordinal), filter);
        Assert.True(filter.Contains("OR (j_Assignment.\"ParentId\" = ANY(@p5)", StringComparison.Ordinal), filter);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_polymorphic_parent_filter_matches_ef_core_and_the_seed(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        var ordersOf = seed.Orders.ToLookup(o => o.CustomerId);
        var customers = seed.Customers.OrderBy(c => c.Id).Take(4).ToList();
        var siteIn = Guid.NewGuid();
        var siteOut = Guid.NewGuid();
        // The visible ids: the first customer's live orders, the third customer, the site in scope.
        var ids = ordersOf[customers[0].Id].Where(o => !o.IsDeleted).Select(o => o.Id).Append(customers[2].Id).Append(siteIn).ToArray();

        var assignments = new List<Assignment>
        {
            new CustomerAssignment { Id = Guid.NewGuid(), ParentId = customers[0].Id, ParentType = ParentKind.Customer, Note = "c0", Role = "owner" },
            new CustomerAssignment { Id = Guid.NewGuid(), ParentId = customers[1].Id, ParentType = ParentKind.Customer, Note = "c1", Role = "guest" },
            new OrderAssignment { Id = Guid.NewGuid(), ParentId = ordersOf[customers[2].Id].First().Id, ParentType = ParentKind.Order, Note = "o2" },
            new OrderAssignment { Id = Guid.NewGuid(), ParentId = ordersOf[customers[3].Id].First().Id, ParentType = ParentKind.Order, Note = "o3" },
            new SiteAssignment { Id = Guid.NewGuid(), ParentId = siteIn, ParentType = ParentKind.Site, Note = "s-in", Floor = 1 },
            new SiteAssignment { Id = Guid.NewGuid(), ParentId = siteOut, ParentType = ParentKind.Site, Note = "s-out", Floor = 2 },
        };
        foreach (var assignment in assignments)
            await session.InsertAsync(assignment); // the root's writer: the discriminator from the row's type
        var reminders = assignments.Select(a => new Reminder { Id = Guid.NewGuid(), AssignmentId = a.Id, Text = a.Note }).ToList();
        reminders.Add(new Reminder { Id = Guid.NewGuid(), AssignmentId = assignments[0].Id, Text = "c0 again" });
        await session.InsertManyAsync(reminders);

        var ours = await session.Query<Reminder>().Where((r, ids) =>
            r.Assignment is CustomerAssignment
                ? ((CustomerAssignment)r.Assignment).Parent.Orders.Count == 0 || ((CustomerAssignment)r.Assignment).Parent.Orders.All(o => !o.IsDeleted && ids.Contains(o.Id))
            : r.Assignment is OrderAssignment
                ? ids.Contains(((OrderAssignment)r.Assignment).Parent.CustomerId)
            : r.Assignment.ParentType == ParentKind.Site
                ? ids.Contains(r.Assignment.ParentId)
            : true, ids).Select(r => r.Id).ToListAsync();
        var theirs = dialect == SqlDialect.MySql ? [] : await Oracles.EfOracle.VisibleReminders(ef, ids); // Oracle's EF provider fails on this query's list parameter (a NullReferenceException in its parameter binding)

        // The seed's answer, computed in memory: the first customer's reminders when none of its orders is deleted, the third customer's order, the site in scope.
        var expected = reminders.Where(r => assignments.First(a => a.Id == r.AssignmentId) switch
        {
            CustomerAssignment c => !ordersOf[c.ParentId].Any() || ordersOf[c.ParentId].All(o => !o.IsDeleted && ids.Contains(o.Id)),
            OrderAssignment o => ids.Contains(seed.Orders.First(x => x.Id == o.ParentId).CustomerId),
            SiteAssignment s => ids.Contains(s.ParentId),
            _ => true,
        }).Select(r => r.Id).ToList();
        Assert.NotEmpty(expected);
        Assert.NotEqual(reminders.Count, expected.Count);
        Assert.Equal(expected.OrderBy(id => id), ours.OrderBy(id => id));
        if (dialect != SqlDialect.MySql) // the seed's answer above is the check on MySQL
            Assert.Equal(theirs.OrderBy(id => id), ours.OrderBy(id => id));

        // OfType reads the derived type's rows as that type; a type test counts them; the parent kind compares through its converter.
        var customerAssignments = await session.Query<Assignment>().OfType<CustomerAssignment>().OrderBy(a => a.Note).ToListAsync();
        Assert.Equal(["owner", "guest"], customerAssignments.Select(a => a.Role));
        Assert.Equal(2, await session.Query<Assignment>().CountAsync(a => a is SiteAssignment));
        Assert.Equal(4, await session.Query<Assignment>().CountAsync(a => a is not SiteAssignment));
        Assert.Equal(2, await session.Query<Assignment>().CountAsync(a => a.ParentType == ParentKind.Order));
        Assert.Equal(1, await session.Query<Assignment>().CountAsync(a => a is SiteAssignment ? ((SiteAssignment)a).Floor > 1 : false));
    }
}
