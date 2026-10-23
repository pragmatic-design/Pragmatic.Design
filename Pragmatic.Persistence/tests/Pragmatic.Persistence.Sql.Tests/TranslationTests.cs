using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The SQL the generator produced for the call sites in this file, read off the plan before it runs:
///     columns quoted, every value a parameter (paging too), joins from navigations, paging per dialect.
///     These tests need no database: the session's connection is never opened.
/// </summary>
public class TranslationTests
{
    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_where_with_the_callers_values_as_the_argument_becomes_parameters_never_inlined()
    {
        var customerId = Guid.NewGuid();
        var minimum = 100m;
        // Two values: a tuple argument, its members read in the predicate; the constants (the status, the bit) are fields of the plan.
        var query = Session(SqlDialect.PostgreSql).Query<Order>()
            .Where((o, a) => o.CustomerId == a.customerId && o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > a.minimum, (customerId, minimum));
        var sql = query.ToSql();
        Assert.StartsWith("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (", sql, StringComparison.Ordinal);
        Assert.Contains("(t.\"CustomerId\" = @p0)", sql, StringComparison.Ordinal);
        Assert.Contains("(t.\"Status\" = @p1)", sql, StringComparison.Ordinal);
        Assert.Contains("NOT (t.\"IsDeleted\" = @p2)", sql, StringComparison.Ordinal); // a bare bit column is compared to true: SQL Server has no boolean expressions
        Assert.Contains("(t.\"Total\" > @p3)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("100", sql, StringComparison.Ordinal);
        using var command = query.ToCommand();
        Assert.Equal(sql, command.CommandText);
        Assert.Equal([customerId, (int)OrderStatus.Open, true, 100m], Values(command));
    }

    [Fact]
    public void Order_paging_and_a_navigation_join_follow_the_dialect()
    {
        var customerId = Guid.NewGuid();
        var postgres = Session(SqlDialect.PostgreSql).Query<Order>().Where((o, customerId) => o.CustomerId == customerId, customerId).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number).Skip(20).Take(10)
            .Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name });
        // Order.CustomerId is a Guid, not a Guid?: the customer is required, so the navigation joins INNER (as EF Core does for a required relationship).
        Assert.Equal("SELECT t.\"Id\", t.\"Number\", t.\"Total\", j_Customer.\"Name\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"CustomerId\" = @p0) ORDER BY t.\"CreatedAt\" DESC, t.\"Number\" LIMIT @p2 OFFSET @p1", postgres.ToSql());
        using var command = postgres.ToCommand();
        Assert.Equal([customerId, 20, 10], Values(command)); // paging values are parameters: one plan per call site, whatever the page

        var sqlServer = Session(SqlDialect.SqlServer).Query<Order>().Where((o, customerId) => o.CustomerId == customerId, customerId).OrderBy(o => o.Number).Skip(20).Take(10);
        Assert.Equal("SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted] FROM [Orders] t WHERE (t.[CustomerId] = @p0) ORDER BY t.[Number] OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY", sqlServer.ToSql());

        var sqlite = Session(SqlDialect.Sqlite).Query<Order>().OrderBy(o => o.Number).Take(5);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t ORDER BY t.\"Number\" LIMIT @p0", sqlite.ToSql());
    }

    [Fact]
    public void Strings_and_lists_translate_to_like_lower_and_in()
    {
        var text = "42";
        var countries = new[] { "IT", "DE" };
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var query = Session(SqlDialect.PostgreSql).Query<Customer>()
            .Where((c, text) => c.Name.Contains(text) || c.Email.StartsWith(text) || c.Country.EndsWith(text), text)
            .Where((c, text) => c.Name.ToLower() == text.ToLower(), text) // a method on the argument runs in .NET at bind time
            .Where((c, a) => a.countries.Contains(c.Country) && a.ids.Contains(c.Id), (countries, ids))
            .Where(c => string.IsNullOrEmpty(c.Email));
        var plan = query.ToSql();
        Assert.True(plan.Contains("t.\"Country\" = ANY(@p4)", StringComparison.Ordinal), plan); // PostgreSQL: the list is one array parameter, the text constant
        using var command = query.ToCommand();
        var sql = command.CommandText;
        Assert.True(sql.Contains("(t.\"Name\" LIKE @p0) OR (t.\"Email\" LIKE @p1)) OR (t.\"Country\" LIKE @p2)", StringComparison.Ordinal), sql);
        Assert.True(sql.Contains("LOWER(t.\"Name\") = @p3", StringComparison.Ordinal), sql);
        Assert.True(sql.Contains("t.\"Country\" = ANY(@p4)", StringComparison.Ordinal), sql);
        Assert.True(sql.Contains("t.\"Id\" = ANY(@p5)", StringComparison.Ordinal), sql);
        Assert.Same(countries, command.Parameters["@p4"].Value); // the array itself, Npgsql maps it
        // SQLite and SQL Server expand the list per call: one placeholder per item, NULL for none.
        var sqlite = Session(SqlDialect.Sqlite).Query<Customer>().Where((c, a) => a.countries.Contains(c.Country) && a.ids.Contains(c.Id), (countries, ids));
        Assert.True(sqlite.ToSql().Contains("t.\"Country\" IN ({0})", StringComparison.Ordinal), sqlite.ToSql()); // the plan keeps a list's place: its text depends on the count
        using var expanded = sqlite.ToCommand();
        Assert.True(expanded.CommandText.Contains("t.\"Country\" IN (@p0_0, @p0_1)", StringComparison.Ordinal), expanded.CommandText);
        Assert.True(expanded.CommandText.Contains("t.\"Id\" IN (@p1_0, @p1_1)", StringComparison.Ordinal), expanded.CommandText);
        Assert.True(sql.Contains("(t.\"Email\" IS NULL OR t.\"Email\" = '')", StringComparison.Ordinal), sql);
        Assert.Equal("%42%", command.Parameters["@p0"].Value);
        Assert.Equal("42%", command.Parameters["@p1"].Value);
        Assert.Equal("%42", command.Parameters["@p2"].Value);
        Assert.Equal("42", command.Parameters["@p3"].Value);
        Assert.Same(ids, command.Parameters["@p5"].Value); // PostgreSQL: the list itself
        Assert.Equal("IT", expanded.Parameters["@p0_0"].Value); // SQLite: one parameter per item
        Assert.Equal("DE", expanded.Parameters["@p0_1"].Value);
        Assert.Equal(ids[0].ToString(), expanded.Parameters["@p1_0"].Value); // a Guid is text there
        Assert.Equal(ids[1].ToString(), expanded.Parameters["@p1_1"].Value);
    }

    [Fact]
    public void Null_comparisons_become_is_null_and_an_anonymous_projection_reads_its_columns()
    {
        var query = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => o.Customer != null).Select(o => new { o.Id, Who = o.Customer!.Name });
        var sql = query.ToSql();
        Assert.Equal("SELECT t.\"Id\", j_Customer.\"Name\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"CustomerId\" IS NOT NULL)", sql);
    }

    [Fact]
    public void An_optional_navigation_joins_left_unless_the_predicate_needs_the_joined_row()
    {
        // A nullable foreign key: LEFT JOIN, the row stays when there is no customer.
        var optional = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Select(o => new { o.Id, Who = o.Customer!.Name });
        Assert.Equal("SELECT t.\"Id\", j_Customer.\"Name\" FROM \"Orders\" t LEFT JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\"", optional.ToSql());

        // The predicate asks for the customer (IS NOT NULL on the key, or a comparison on its columns, in an AND): the join can be INNER, which frees the optimizer.
        var present = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Where(o => o.Customer != null).Select(o => new { o.Id, Who = o.Customer!.Name });
        Assert.Equal("SELECT t.\"Id\", j_Customer.\"Name\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"CustomerId\" IS NOT NULL)", present.ToSql());
        var compared = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Where((o, name) => o.Customer!.Name == name && o.Total > 0m, "x").Select(o => o.Id);
        Assert.StartsWith("SELECT t.\"Id\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer", compared.ToSql(), StringComparison.Ordinal);

        // Under an OR, a NOT or an IS NULL the row may be wanted without its customer: LEFT stays.
        var either = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Where((o, name) => o.Customer!.Name == name || o.Number == "x", "x").Select(o => o.Id);
        Assert.StartsWith("SELECT t.\"Id\" FROM \"Orders\" t LEFT JOIN \"Customers\" j_Customer", either.ToSql(), StringComparison.Ordinal);
        var missing = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Where(o => o.Customer!.Name == null).Select(o => o.Id);
        Assert.StartsWith("SELECT t.\"Id\" FROM \"Orders\" t LEFT JOIN \"Customers\" j_Customer", missing.ToSql(), StringComparison.Ordinal);
        var negated = Session(SqlDialect.PostgreSql).Query<OptionalCustomerOrder>().Where((o, name) => !(o.Customer!.Name == name), "x").Select(o => o.Id);
        Assert.StartsWith("SELECT t.\"Id\" FROM \"Orders\" t LEFT JOIN \"Customers\" j_Customer", negated.ToSql(), StringComparison.Ordinal);

        // The runtime path picks the same join kinds from the registered maps (a scalar Select: an anonymous projection needs a [MapFrom] DTO there).
        var session = Session(SqlDialect.PostgreSql);
        var name = "x";
        IQueryable<OptionalCustomerOrder> runtime = session.Query<OptionalCustomerOrder>();
        Assert.Equal(compared.ToSql(), runtime.Where(o => o.Customer!.Name == name && o.Total > 0m).Select(o => o.Id).ToSql());
        Assert.Equal(either.ToSql(), runtime.Where(o => o.Customer!.Name == name || o.Number == "x").Select(o => o.Id).ToSql());
        Assert.Equal(missing.ToSql(), runtime.Where(o => o.Customer!.Name == null).Select(o => o.Id).ToSql());
        IQueryable<Order> orders = session.Query<Order>();
        Assert.Contains("INNER JOIN", orders.Where(o => o.Customer!.Name == name || o.Number == "x").Select(o => o.Id).ToSql(), StringComparison.Ordinal); // required key: INNER even under an OR
    }

    [Fact]
    public void Include_hydrates_a_navigation_from_the_join_and_a_declared_projection_needs_no_lambda()
    {
        var id = Guid.NewGuid();
        var withCustomer = Session(SqlDialect.PostgreSql).Query<Order>().Include(o => o.Customer).Where((o, id) => o.Id == id, id);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\", j_Customer.\"Id\", j_Customer.\"Name\", j_Customer.\"Email\", j_Customer.\"Country\", j_Customer.\"CreatedAt\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"Id\" = @p0)", withCustomer.ToSql());

        // Project<TDto>: Id, Number and Total are columns; CustomerName is the Customer navigation's Name by convention.
        var declared = Session(SqlDialect.SqlServer).Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.Number).Take(20).Project<OrderRowDto>();
        Assert.Equal("SELECT t.[Id], t.[Number], t.[Total], j_Customer.[Name] FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE (NOT (t.[IsDeleted] = @p0)) ORDER BY t.[Number] OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY", declared.ToSql());

        // A collection include is a companion statement in the same command: the lines whose order is among the keys the rows' statement selects (one round trip).
        var withLines = Session(SqlDialect.Sqlite).Query<Order>().Include(o => o.Lines).Where((o, id) => o.Id == id, id);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"Id\" = @p0); "
            + "SELECT t.\"Id\", t.\"OrderId\", t.\"Sku\", t.\"Quantity\", t.\"UnitPrice\" FROM \"OrderLines\" t WHERE (t.\"OrderId\" IN (SELECT t.\"Id\" FROM \"Orders\" t WHERE (t.\"Id\" = @p0)))", withLines.ToSql());
    }

    [Fact]
    public void A_nested_include_chains_the_joins_and_a_collection_below_adds_a_companion_statement()
    {
        var id = Guid.NewGuid();
        // Two to-one hops in one path: the second join hangs off the first's alias, its columns follow in the select list.
        var twoHops = Session(SqlDialect.PostgreSql).Query<OrderLine>().Include(l => l.Order!.Customer).Where((l, id) => l.OrderId == id, id);
        Assert.Equal("SELECT t.\"Id\", t.\"OrderId\", t.\"Sku\", t.\"Quantity\", t.\"UnitPrice\", j_Order.\"Id\", j_Order.\"CustomerId\", j_Order.\"Number\", j_Order.\"Status\", j_Order.\"Total\", j_Order.\"CreatedAt\", j_Order.\"IsDeleted\", j_Order_Customer.\"Id\", j_Order_Customer.\"Name\", j_Order_Customer.\"Email\", j_Order_Customer.\"Country\", j_Order_Customer.\"CreatedAt\" FROM \"OrderLines\" t INNER JOIN \"Orders\" j_Order ON j_Order.\"Id\" = t.\"OrderId\" INNER JOIN \"Customers\" j_Order_Customer ON j_Order_Customer.\"Id\" = j_Order.\"CustomerId\" WHERE (t.\"OrderId\" = @p0)", twoHops.ToSql());

        // A to-one, then a collection below it: the join is in the rows' statement; the collection's companion selects the children of the included objects' keys, through the same join.
        var ordersOfCustomer = Session(SqlDialect.SqlServer).Query<Order>().Include(o => o.Customer.Then(c => c.Orders)).Where((o, id) => o.Id == id, id);
        Assert.Equal("SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted], j_Customer.[Id], j_Customer.[Name], j_Customer.[Email], j_Customer.[Country], j_Customer.[CreatedAt] FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE (t.[Id] = @p0); "
            + "SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted] FROM [Orders] t WHERE (t.[CustomerId] IN (SELECT j_Customer.[Id] FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE (t.[Id] = @p0)))", ordersOfCustomer.ToSql());

        // A collection, then a collection below its elements: two companions, the second nesting the first's subquery; a filter on the collection goes into its companion (and so into the subquery below it).
        var twoLevels = Session(SqlDialect.Sqlite).Query<Customer>().Include(c => c.Orders.Then(o => o.Lines)).Where((c, id) => c.Id == id, id);
        Assert.Equal("SELECT t.\"Id\", t.\"Name\", t.\"Email\", t.\"Country\", t.\"CreatedAt\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0); "
            + "SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0))); "
            + "SELECT t.\"Id\", t.\"OrderId\", t.\"Sku\", t.\"Quantity\", t.\"UnitPrice\" FROM \"OrderLines\" t WHERE (t.\"OrderId\" IN (SELECT t.\"Id\" FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0)))))", twoLevels.ToSql());
        var filtered = Session(SqlDialect.Sqlite).Query<Customer>().Include(c => c.Orders.Where(o => !o.IsDeleted).Then(o => o.Lines)).Where((c, id) => c.Id == id, id);
        Assert.Equal("SELECT t.\"Id\", t.\"Name\", t.\"Email\", t.\"Country\", t.\"CreatedAt\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0); "
            + "SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0))) AND (NOT (t.\"IsDeleted\" = @p1)); "
            + "SELECT t.\"Id\", t.\"OrderId\", t.\"Sku\", t.\"Quantity\", t.\"UnitPrice\" FROM \"OrderLines\" t WHERE (t.\"OrderId\" IN (SELECT t.\"Id\" FROM \"Orders\" t WHERE (t.\"CustomerId\" IN (SELECT t.\"Id\" FROM \"Customers\" t WHERE (t.\"Id\" = @p0))) AND (NOT (t.\"IsDeleted\" = @p1))))", filtered.ToSql());
    }

    [Fact]
    public void A_declared_projection_with_a_collection_adds_a_projected_companion_and_an_aggregate_a_correlated_subquery()
    {
        var id = Guid.NewGuid();
        var session = Session(SqlDialect.Sqlite);
        // The DTO's aggregates are subqueries in the select list; its List<OrderLineDto> is a companion selecting the child DTO's columns and the foreign key to group on.
        var withLines = session.Query<Order>().Where((o, id) => o.Id == id, id).Project<OrderWithLinesDto>();
        const string parent = "SELECT t.\"Id\", t.\"Number\", j_Customer.\"Name\", (SELECT COUNT(*) FROM \"OrderLines\" c WHERE c.\"OrderId\" = t.\"Id\"), (SELECT COALESCE(SUM(c.\"Quantity\"), 0) FROM \"OrderLines\" c WHERE c.\"OrderId\" = t.\"Id\"), (SELECT MAX(c.\"UnitPrice\") FROM \"OrderLines\" c WHERE c.\"OrderId\" = t.\"Id\") FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"Id\" = @p0)";
        Assert.Equal(parent + "; SELECT t.\"Id\", t.\"Sku\", t.\"Quantity\", t.\"OrderId\" FROM \"OrderLines\" t WHERE (t.\"OrderId\" IN (SELECT t.\"Id\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (t.\"Id\" = @p0)))", withLines.ToSql());

        // The runtime path builds the same command from the registered projection.
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(withLines.ToSql(), composed.Where(o => o.Id == id).Project<Order, OrderWithLinesDto>().ToSql());

        // Two levels: the grandchildren's companion nests the children's subquery, as the include tree does; SQL Server quoting reaches into the subqueries.
        var customer = Session(SqlDialect.SqlServer).Query<Customer>().Where((c, id) => c.Id == id, id).Project<CustomerWithOrdersDto>();
        Assert.Equal("SELECT t.[Id], t.[Name], (SELECT COUNT(*) FROM [Orders] c WHERE c.[CustomerId] = t.[Id]) FROM [Customers] t WHERE (t.[Id] = @p0); "
            + "SELECT t.[Id], t.[Number], j_Customer.[Name], (SELECT COUNT(*) FROM [OrderLines] c WHERE c.[OrderId] = t.[Id]), (SELECT COALESCE(SUM(c.[Quantity]), 0) FROM [OrderLines] c WHERE c.[OrderId] = t.[Id]), (SELECT MAX(c.[UnitPrice]) FROM [OrderLines] c WHERE c.[OrderId] = t.[Id]), t.[CustomerId] FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE (t.[CustomerId] IN (SELECT t.[Id] FROM [Customers] t WHERE (t.[Id] = @p0))); "
            + "SELECT t.[Id], t.[Sku], t.[Quantity], t.[OrderId] FROM [OrderLines] t WHERE (t.[OrderId] IN (SELECT t.[Id] FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] WHERE (t.[CustomerId] IN (SELECT t.[Id] FROM [Customers] t WHERE (t.[Id] = @p0)))))", customer.ToSql());
    }

    [Fact]
    public void A_cursor_is_a_row_value_comparison_where_the_dialect_has_it_and_the_expanded_form_elsewhere()
    {
        var cursor = (DateTimeOffset.UtcNow, Guid.NewGuid());
        // Two ascending keys on PostgreSQL: a row value; the cursor's items are parameters after the predicate's, before the paging.
        var ascending = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).After(cursor).Take(20);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) AND ((t.\"CreatedAt\", t.\"Id\") > (@p1, @p2)) ORDER BY t.\"CreatedAt\", t.\"Id\" LIMIT @p3", ascending.ToSql());
        using var command = ascending.ToCommand();
        Assert.Equal([true, cursor.Item1, cursor.Item2, 20], Values(command)); // the bare boolean compares to true; then the cursor's two items; then the page size

        // Mixed directions (and SQL Server, which has no row values): the expanded form.
        var mixedCursor = (123.45m, Guid.NewGuid());
        var mixed = Session(SqlDialect.SqlServer).Query<Order>().Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).ThenBy(o => o.Id).After(mixedCursor).Take(20);
        Assert.Equal("SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted] FROM [Orders] t WHERE (NOT (t.[IsDeleted] = @p0)) AND (t.[Total] < @p1 OR (t.[Total] = @p1 AND t.[Id] > @p2)) ORDER BY t.[Total] DESC, t.[Id] OFFSET 0 ROWS FETCH NEXT @p3 ROWS ONLY", mixed.ToSql());
        var mixedOnSqlite = Session(SqlDialect.Sqlite).Query<Order>().Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).ThenBy(o => o.Id).After(mixedCursor).Take(20);
        Assert.Contains("(t.\"Total\" < @p1 OR (t.\"Total\" = @p1 AND t.\"Id\" > @p2))", mixedOnSqlite.ToSql(), StringComparison.Ordinal);

        // One key: the value itself.
        var id = Guid.NewGuid();
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"Id\" > @p0) ORDER BY t.\"Id\" LIMIT @p1", Session(SqlDialect.Sqlite).Query<Order>().OrderBy(o => o.Id).After(id).Take(20).ToSql());

        // The runtime path builds the same statements.
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Order> composed = session.Query<Order>();
        Assert.Equal(ascending.ToSql(), composed.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).After(cursor).Take(20).ToSql());
        IQueryable<Order> composedMixed = Session(SqlDialect.SqlServer).Query<Order>();
        Assert.Equal(mixed.ToSql(), composedMixed.Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).ThenBy(o => o.Id).After(mixedCursor).Take(20).ToSql());
    }

    [Fact]
    public void A_batch_joins_the_declared_queries_statements_and_renumbers_their_parameters()
    {
        var session = Session(SqlDialect.PostgreSql);
        var batch = session.Batch();
        batch.ById(Guid.NewGuid());
        batch.CountOpenAbove(1000m);
        batch.OpenRows(Guid.NewGuid(), 2, 20);
        // Three statements, one text: the second's parameters start after the first's, the third's after the second's (its paging included).
        var sql = batch.ToSql();
        var statements = sql.Split("; ");
        Assert.Equal(3, statements.Length);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"Id\" = @p0) LIMIT 1", statements[0]);
        Assert.StartsWith("SELECT COUNT(*) FROM \"Orders\" t WHERE ", statements[1], StringComparison.Ordinal);
        Assert.Equal(["@p1", "@p2", "@p3"], System.Text.RegularExpressions.Regex.Matches(statements[1], "@p\\d+").Select(m => m.Value).ToArray());
        Assert.StartsWith("SELECT t.\"Id\", t.\"Number\", t.\"Total\", j_Customer.\"Name\" FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer", statements[2], StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY t.\"CreatedAt\" DESC LIMIT @p8 OFFSET @p7", statements[2], StringComparison.Ordinal);
        Assert.Equal(["@p4", "@p5", "@p6", "@p8", "@p7"], System.Text.RegularExpressions.Regex.Matches(statements[2], "@p\\d+").Select(m => m.Value).ToArray());
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public void A_visibility_rule_is_in_every_plan_unless_the_chain_reads_past_it()
    {
        var customerId = Guid.NewGuid();
        var live = Session(SqlDialect.PostgreSql).Query<LiveOrder>().Where((o, customerId) => o.CustomerId == customerId, customerId);
        var sql = live.ToSql();
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"CustomerId\" = @p0) AND (NOT (t.\"IsDeleted\" = @p1))", sql);
        using var command = live.ToCommand();
        Assert.Equal([customerId, true], Values(command)); // the rule's value is bound like a captured one, from the rule's tree built once

        var all = Session(SqlDialect.PostgreSql).Query<LiveOrder>().WithoutFilters().Where((o, customerId) => o.CustomerId == customerId, customerId);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE (t.\"CustomerId\" = @p0)", all.ToSql());
    }

    [Fact]
    public void A_chain_composed_at_runtime_translates_to_the_sql_of_its_compile_time_twin()
    {
        var customerId = Guid.NewGuid();
        var session = Session(SqlDialect.PostgreSql);
        // Typed as the interface, the chain binds to System.Linq: the tree is translated at runtime with the same rules (PRAG3005 says so).
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(10);
        var compiled = session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).Skip(20).Take(10);
        Assert.Equal(compiled.ToSql(), composed.ToSql());

        // A runtime Select into a [MapFrom] DTO reads the registered projection, the same columns Project<TDto>() emits.
        var rows = composed.Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name });
        Assert.Equal(compiled.Project<OrderRowDto>().ToSql(), rows.ToSql());
        Assert.Equal(compiled.Project<OrderRowDto>().ToSql(), composed.Project<Order, OrderRowDto>().ToSql()); // the AOT-clean form: no tree with an initializer

        // SQL Server quoting and a visibility rule follow the runtime path too.
        IQueryable<LiveOrder> live = Session(SqlDialect.SqlServer).Query<LiveOrder>();
        Assert.Equal("SELECT t.[Id], t.[CustomerId], t.[Number], t.[Status], t.[Total], t.[CreatedAt], t.[IsDeleted] FROM [Orders] t WHERE (t.[CustomerId] = @p0) AND (NOT (t.[IsDeleted] = @p1))", live.Where(o => o.CustomerId == customerId).ToSql());
    }

    [Fact]
    public void A_group_by_reads_its_key_and_aggregates_in_one_statement()
    {
        var grouped = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId)
            .Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total), Largest = g.Max(o => o.Total), Mean = g.Average(o => o.Total) });
        Assert.Equal("SELECT t.\"CustomerId\", COUNT(*), SUM(t.\"Total\"), MAX(t.\"Total\"), AVG(t.\"Total\") FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) GROUP BY t.\"CustomerId\"", grouped.ToSql());

        var byStatus = Session(SqlDialect.SqlServer).Query<Order>().GroupBy(o => o.Status).Select(g => new StatusCount { Status = g.Key, Count = g.Count() }).Take(3);
        Assert.Equal("SELECT t.[Status], COUNT(*) FROM [Orders] t GROUP BY t.[Status] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT @p0 ROWS ONLY", byStatus.ToSql());

        // A Where on the groups is HAVING (its value a parameter), an OrderBy on the groups orders by the aggregate.
        var atLeast = 100;
        var busiest = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Where((g, atLeast) => g.Count() > atLeast && g.Sum(o => o.Total) > 0m, atLeast)
            .OrderByDescending(g => g.Sum(o => o.Total)).ThenBy(g => g.Key).Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).Take(5);
        Assert.Equal("SELECT t.\"CustomerId\", COUNT(*), SUM(t.\"Total\") FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) GROUP BY t.\"CustomerId\" HAVING ((COUNT(*) > @p1) AND (SUM(t.\"Total\") > @p2)) ORDER BY SUM(t.\"Total\") DESC, t.\"CustomerId\" LIMIT @p3", busiest.ToSql());
        using var command = busiest.ToCommand();
        Assert.Equal([true, 100, 0m, 5], Values(command));
    }

    [Fact]
    public void A_runtime_include_translates_to_the_sql_of_its_compile_time_twin()
    {
        var id = Guid.NewGuid();
        var session = Session(SqlDialect.PostgreSql);
        // Include on an IQueryable<T> chain: the extension builds the node, the planner joins and hydrates from the registered maps.
        IQueryable<OrderLine> lines = session.Query<OrderLine>();
        var runtime = lines.Include(l => l.Order!.Customer).Where(l => l.OrderId == id);
        var compiled = session.Query<OrderLine>().Include(l => l.Order!.Customer).Where((l, id) => l.OrderId == id, id);
        Assert.Equal(compiled.ToSql(), runtime.ToSql());

        IQueryable<Order> orders = session.Query<Order>();
        Assert.Equal(session.Query<Order>().Include(o => o.Lines).Where((o, id) => o.Id == id, id).ToSql(), orders.Include(o => o.Lines).Where(o => o.Id == id).ToSql());
    }

    public sealed class StatusCount
    {
        public OrderStatus Status { get; set; }
        public int Count { get; set; }
    }

    [Fact]
    public void Terminals_shape_the_statement_and_a_branch_keeps_its_base()
    {
        var customerId = Guid.NewGuid();
        var open = Session(SqlDialect.Sqlite).Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId);
        using var count = open.Skip(3).ToCommand();
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE ((t.\"CustomerId\" = @p0) AND (NOT (t.\"IsDeleted\" = @p1))) LIMIT -1 OFFSET @p2", count.CommandText);
        Assert.Equal([customerId.ToString(), 1, 3], Values(count)); // SQLite stores guids as text and booleans as integers
        using var again = open.ToCommand();
        Assert.Equal(2, again.Parameters.Count); // the base chain was not touched by the Skip on its branch
    }

    [Fact]
    public async Task An_operator_the_generator_did_not_translate_fails_loudly_instead_of_running_in_memory()
    {
        // A queryable handed over as IQueryable<T> runs on the runtime path: enumerating it synchronously is refused, and an
        // operator outside the subset (Reverse) fails at the terminal with the reason. Nothing ever runs in memory.
        var queryable = HandedOver(Session(SqlDialect.Sqlite));
        var sync = Assert.Throws<NotSupportedException>(() => queryable.Reverse().ToList());
        Assert.Contains("async terminals", sync.Message, StringComparison.Ordinal);
        var outside = await Assert.ThrowsAsync<NotSupportedException>(() => queryable.Reverse().ToListAsync().AsTask());
        Assert.Contains("'Reverse' is not in the translated subset", outside.Message, StringComparison.Ordinal);
    }

    private static IQueryable<Order> HandedOver(SqlSession session) => session.Query<Order>();

    [Fact]
    public void Plan_ids_are_one_sequence_across_row_types()
    {
        // A session caches one prepared command per plan id: two plans of different row types with the same id would
        // share a command (found by `verify`: the entity plan's command served the DTO plan). The sequence is global.
        var orders = new SqlPlan<Order>("", "", "", [], static (in SqlQuery<Order> q, System.Data.Common.DbParameterCollection p, SqlDialect d) => { }, null);
        var rows = new SqlPlan<OrderRowDto>("", "", "", [], static (in SqlQuery<OrderRowDto> q, System.Data.Common.DbParameterCollection p, SqlDialect d) => { }, null);
        Assert.NotEqual(orders.Id, rows.Id);
    }

    private static object?[] Values(System.Data.Common.DbCommand command)
    {
        var values = new object?[command.Parameters.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = command.Parameters[i].Value;
        return values;
    }
}
