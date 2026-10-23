using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.DiagnosticsFixture;

// One call site per diagnostic. The tests assert each code is reported, with its line, when this project is built.

/// <summary>PRAG3003: a property type the reader does not map (and no converter for it).</summary>
[Entity]
public sealed class WithUri
{
    public Guid Id { get; set; }
    public Uri Link { get; set; } = new("https://example.test");
}

/// <summary>PRAG3004: a navigation without its foreign key property.</summary>
[Entity]
public sealed class WithoutKey
{
    public Guid Id { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>PRAG3007: an identity on a composite key, which no database assigns.</summary>
[Entity]
public sealed class IdentityOnComposite
{
    [Key]
    [Identity]
    public long Serial { get; set; }

    [Key]
    public string Region { get; set; } = "";
}

/// <summary>PRAG3002: a DTO with a collection member but no property reading the parent's key, which the loader groups the children on.</summary>
[MapFrom<Order>]
public sealed class LinesWithoutKeyDto
{
    public string Number { get; set; } = "";
    public List<OrderLineDto> Lines { get; set; } = [];
}

/// <summary>A DTO two Selects assign in different orders (the set operation's members must agree).</summary>
public sealed class NumberTotal
{
    public string Number { get; set; } = "";
    public decimal Total { get; set; }
}

public static class Outside
{
    public static async Task Run(SqlSession session, Guid id)
    {
        // PRAG3001: a filter after the projection, which the subset does not translate.
        var afterSelect = await session.Query<Order>().Select(o => o.Number).Where(n => n.Length > 0).ToListAsync();
        _ = afterSelect;

        // PRAG3002: a type test on an entity outside a hierarchy in one table (nothing to test: no discriminator).
        var typeTest = await session.Query<Order>().Where(o => o is Order).ToListAsync();
        _ = typeTest;

        // PRAG3002: a window function in a predicate (SQL evaluates windows after the WHERE: DistinctOn filters on one).
        var windowed = await session.Query<Order>().Where(o => Sql.RowNumber(o.Number) == 1).ToListAsync();
        _ = windowed;

        // PRAG3002: a moving frame's row count from a variable (a frame takes no parameter: the count is written into the statement).
        var preceding = 2;
        var moving = await session.Query<Order>().Select(o => new { o.Id, M = Sql.SumOver(o.Total, o.CustomerId, o.Number, preceding) }).ToListAsync();
        _ = moving;

        // PRAG3001: an update through a chain on a view ([Table(ReadOnly = true)]).
        var viewUpdate = await session.Query<CustomerSummary>().Where(s => s.Orders == 0).ExecuteUpdateAsync(s => s.SetProperty(v => v.Name, "none"));
        _ = viewUpdate;

        // PRAG3001: a delete through a chain on the root of a table-per-concrete-type hierarchy (a view over the types' tables).
        var tpcDelete = await session.Query<Payment>().Where(p => p.Amount == 0m).ExecuteDeleteAsync();
        _ = tpcDelete;

        // PRAG3002: a type test on a derived type of a table-per-concrete-type hierarchy (its rows are all of that type).
        var tpcTest = await session.Query<CardPayment>().Where(p => p is CardPayment).ToListAsync();
        _ = tpcTest;

        // PRAG3002: a Take per parent in an Include without an ordering (the window needs one), and one with a value of the caller.
        var untaken = await session.Query<Customer>().Include(c => c.Orders.Take(2)).ToListAsync();
        _ = untaken;
        var howMany = 2;
        var takenByValue = await session.Query<Customer>().Include(c => c.Orders.OrderBy(o => o.Number).Take(howMany)).ToListAsync();
        _ = takenByValue;

        // PRAG3001: ForUpdate on an aggregate (the lock is on rows the statement returns).
        var lockedCount = await session.Query<Order>().ForUpdate().CountAsync();
        _ = lockedCount;

        // PRAG3001: OfType to a type that is not a derived type of the hierarchy's root (the root itself here).
        var notDerived = await session.Query<Charge>().OfType<Charge>().ToListAsync();
        _ = notDerived;

        // PRAG3002: an Include path that goes on past a collection with a member that is not a navigation (Then is for going below it).
        var pastCollection = await session.Query<Customer>().Include(c => c.Orders.Count).ToListAsync();
        _ = pastCollection;

        // PRAG3002: an Include filter with a value of the caller (the child query is shared by every call: columns and constants only).
        var since = DateTimeOffset.UtcNow;
        var filteredByValue = await session.Query<Customer>().Include(c => c.Orders.Where(o => o.CreatedAt > since)).ToListAsync();
        _ = filteredByValue;

        // CS0311 (the compiler, not the generator): Then is constrained to ISqlEntity, so it does not bind on a string (the navigation overload is picked over IEnumerable<char>, and string is no entity).
        var thenOnValue = await session.Query<Order>().Include(o => o.Number.Then(n => n.ToString())).ToListAsync();
        _ = thenOnValue;

        // PRAG3002: a local captured by the lambda, which no tree carries; the remedy is the two-parameter form below.
        var captured = await session.Query<Order>().Where(o => o.Id == id).ToListAsync();
        _ = captured;

        // A chain inside the subset: in a census build it is reported (PRAG3099) with its SQL.
        var fine = await session.Query<Order>().Where((o, id) => o.Id == id, id).ToListAsync();
        _ = fine;

        // PRAG3011 (information, a warning in a census build): a WHERE on a column that is neither the key nor [Indexed] scans the table.
        var scan = await session.Query<Order>().Where(o => o.Total > 100m).ToListAsync();
        _ = scan;

        // PRAG3012 (information, a warning in a census build): a catalog function whose rendering on a dialect is an emulation (dates on SQLite).
        var emulated = await session.Query<Order>().Where(o => Sql.Year(o.CreatedAt) == 2026).CountAsync();
        _ = emulated;

        // PRAG3002: the date part of a catalog function must be a constant (the generator renders it into the statement).
        var part = DatePart.Day;
        var variablePart = await session.Query<Order>().Where((o, id) => Sql.DateDiff(part, o.CreatedAt, o.CreatedAt) == 0 && o.Id == id, id).CountAsync();
        _ = variablePart;

        // The command terminals, reported with their statements in a census build (there is no ToSql on them); two writes in a method with
        // no transaction around them: PRAG3025 (information) at the first.
        var kind = "x";
        var updated = await session.Query<Event>().Where((e, id) => e.OrderId == id, id).ExecuteUpdateAsync((s, kind) => s.SetProperty(e => e.Kind, kind).SetProperty(e => e.Version, e => e.Version + 1), kind);
        var deleted = await session.Query<Event>().Where((e, id) => e.OrderId == id, id).ExecuteDeleteAsync();
        _ = updated + deleted;

        // PRAG3001: streaming cannot load a collection include (the loader needs the rows together).
        var streamed = session.Query<Order>().Include(o => o.Lines).AsAsyncEnumerable();
        _ = streamed;

        // PRAG3002: a cursor without the ordering it continues.
        var unordered = await session.Query<Order>().After(id).Take(20).ToListAsync();
        _ = unordered;

        // PRAG3002: a cursor whose shape is not the keys' (two keys, one value).
        var shortCursor = await session.Query<Order>().OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).After(id).Take(20).ToListAsync();
        _ = shortCursor;

        // PRAG3009 (warning): an ordering that is not total (its last key is not the entity's key), so a cursor may skip or repeat rows.
        var notTotal = await session.Query<Order>().OrderBy(o => o.Number).After("ORD-1").Take(20).ToListAsync();
        _ = notTotal;

        // PRAG3001: a member of a set operation that orders (the dialects disagree on ORDER BY inside a compound select): order the result instead.
        var orderedMember = await session.Query<Order>().OrderBy(o => o.Number).Union(session.Query<Order>().Where(o => o.IsDeleted)).ToListAsync();
        _ = orderedMember;

        // PRAG3001: the members of a set operation assign the DTO's members in different orders: the first member's reader reads every row by ordinal.
        var mismatched = await session.Query<Order>().Select(o => new NumberTotal { Number = o.Number, Total = o.Total }).Union(session.Query<Order>().Select(o => new NumberTotal { Total = o.Total, Number = o.Number })).ToListAsync();
        _ = mismatched;

        // PRAG3001: the inner chain of a join does more than filter (its ordering means nothing to the join).
        var orderedInner = await session.Query<Order>().Join(session.Query<Customer>().OrderBy(c => c.Name), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).ToListAsync();
        _ = orderedInner;

        // PRAG3001: a LEFT JOIN reads the inner row's columns as null, so a value-type member must be nullable.
        var notNullable = await session.Query<Customer>().LeftJoin(session.Query<Order>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Total }).ToListAsync();
        _ = notNullable;

        // PRAG3001: an Include on a joined row (the loader works on the entity's rows).
        var includeAfterJoin = await session.Query<Order>().Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { Order = o, Customer = c }).Include(x => x.Order.Lines).ToListAsync();
        _ = includeAfterJoin;

        // PRAG3001: a member of the joined row that is not one ('Status' is not in the selector).
        var notAMember = await session.Query<Order>().Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).Where(x => x.Number == "ORD-1").Select(x => new { x.Name, Upper = x.Name.ToUpper() }).Where(x => x.Upper == "A").ToListAsync();
        _ = notAMember;

        // PRAG3002: a local captured inside a subquery's lambda (the subquery reads the outer row and the argument, not the method's locals).
        var minimum = 5;
        var capturedInSubquery = await session.Query<Order>().Where(o => session.Query<OrderLine>().Any(l => l.OrderId == o.Id && l.Quantity > minimum)).ToListAsync();
        _ = capturedInSubquery;

        // PRAG3002: an operator a subquery does not take (OrderBy): a subquery is Where, Select for IN, and a scalar terminal.
        var orderedSubquery = await session.Query<Customer>().Where(c => session.Query<Order>().OrderBy(o => o.Number).Any(o => o.CustomerId == c.Id)).ToListAsync();
        _ = orderedSubquery;

        // PRAG3001: a GroupJoin whose group is never read by a SelectMany (the group is not a value).
        var groupLeft = await session.Query<Customer>().GroupJoin(session.Query<Order>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders }).ToListAsync();
        _ = groupLeft;

        // PRAG3001: a correlated chain in a SelectMany with a Take but no OrderBy (which N rows per parent?).
        var unorderedTake = await session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.Id).Take(2), (c, o) => new { c.Name, o.Number }).ToListAsync();
        _ = unorderedTake;

        // PRAG3002: an aggregate chain on a group with a step that is no aggregate step (OrderBy): the aggregates are Where/Select/Distinct then Count/Sum/Min/Max/Average.
        var orderedGroup = await session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, First = g.OrderBy(o => o.Number).Min(o => o.Total) }).ToListAsync();
        _ = orderedGroup;

        // PRAG3021 (information, a warning in a census build): a list read with no Take and no predicate on the key reads every matching row.
        var everyOpen = await session.Query<Order>().Where(o => !o.IsDeleted).ToListAsync();
        _ = everyOpen;
        var countries = await session.Query<LookupCountry>().ToListAsync(); // a [Lookup] table: read whole on purpose, no PRAG3021
        _ = countries;

        // PRAG3022 (information): Contains is a LIKE that starts with a wildcard, which the index on Number (in the schema snapshot) cannot serve.
        var contains = await session.Query<Order>().Where(o => o.Number.Contains("ORD")).Take(5).ToListAsync();
        _ = contains;

        // PRAG3023 (information): a predicate that does not read the row is true for every row or for none.
        var always = await session.Query<Order>().Where(o => 1 == 1).Take(5).ToListAsync();
        _ = always;

        // PRAG3024 (information): the included Customer row is read for one column only.
        var withCustomer = await session.Query<Order>().Include(o => o.Customer).Take(10).ToListAsync();
        foreach (var w in withCustomer)
            Console.WriteLine(w.Customer!.Name);

        // PRAG3026: a hand-written statement against the model: a column the table does not have (renamed in the entity, not in the statement); a statement
        // that does not return every column the row type reads.
        var renamed = await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Totl\" > 0").ToListAsync();
        _ = renamed;
        var narrow = await session.Sql<Order>($"SELECT \"Id\", \"Number\" FROM \"Orders\"").ToListAsync();
        _ = narrow;

        // A bare identifier the model check does not read (the dialect's business): the PragmaticExplain target reports it when the database refuses it (PRAG3020).
        var bare = await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE Totl > 0").Take(5).ToListAsync();
        _ = bare;

        // PRAG3005 (info): a chain typed as IQueryable<T>, translated at runtime.
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => o.Id == id);
        _ = composed;

        // PRAG3010 (warning): a query inside a loop over another query's rows, one per row (N+1): the loop variable feeds the inner chain.
        var customers = await session.Query<Customer>().Take(5).ToListAsync();
        foreach (var customer in customers)
        {
            var perCustomer = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId, customer.Id).ToListAsync();
            _ = perCustomer;
        }
    }
}

/// <summary>PRAG3013: a [SqlFunction] without its definition; and one whose definition has no arm for a dialect (PRAG3002 where it is used).</summary>
public static partial class BadFunctions
{
    [SqlFunction]
    public static partial int NoDefinition(int value);

    [SqlFunction]
    public static partial int TwoDialects(int value);

    private static SqlFragment TwoDialects(SqlDialect dialect, SqlFragment value) => dialect switch
    {
        SqlDialect.Sqlite => value,
        SqlDialect.PostgreSql => value,
    };

    public static async Task Run(SqlSession session)
    {
        var used = await session.Query<Order>().CountAsync(o => BadFunctions.TwoDialects(Sql.Length(o.Number)) == 10);
        _ = used;
    }
}

/// <summary>PRAG3006: a declared chain that calls another declared chain, and one whose body is not a chain.</summary>
public static partial class BadChains
{
    [Query]
    public static SqlQuery<Order> Open(SqlQuery<Order> orders) => orders.Where(o => o.Status == OrderStatus.Open);

    [Query]
    public static SqlQuery<Order> OpenAndBig(SqlQuery<Order> orders, decimal min) => Open(orders).Where(o => o.Total > min);

    [Query]
    public static SqlQuery<Order> NotAChain(SqlQuery<Order> orders) => orders;
}

/// <summary>PRAG3006: a [Query] definition out of shape (the class is not partial).</summary>
public static class NotPartial
{
    [Query]
    private static ValueTask<Order?> ById(SqlQuery<Order> orders, Guid id) => orders.FirstOrDefaultAsync(o => o.Id == id);
}

/// <summary>A lookup table ([Lookup]): read whole on purpose, so its unbounded read is not PRAG3021 (the Order one in Outside is).</summary>
[Entity]
[Table("Customers")]
[Lookup]
public sealed class LookupCountry
{
    public Guid Id { get; set; }
    public string Country { get; set; } = "";
}
