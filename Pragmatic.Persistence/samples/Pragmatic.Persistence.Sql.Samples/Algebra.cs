using Pragmatic.Persistence.Workload;
using Pragmatic.Persistence.Sql.Attributes;
using Pragmatic.Persistence.Sql.Functions;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>An application's own SQL function: declared with [SqlFunction], defined once per dialect with typed fragments, read by the generator and never run.</summary>
internal static partial class AppSql
{
    /// <summary>Whole days from one instant to another: DATEDIFF on SQL Server, TIMESTAMPDIFF on MySQL, EPOCH arithmetic on PostgreSQL, julianday on SQLite (an emulation, said so).</summary>
    [SqlFunction]
    public static partial int DaysBetween(DateTimeOffset from, DateTimeOffset to);

    private static SqlFragment DaysBetween(SqlDialect dialect, SqlFragment from, SqlFragment to) => dialect switch
    {
        SqlDialect.SqlServer => Fragment.Call("DATEDIFF", Fragment.Keyword("day"), from, to),
        SqlDialect.MySql => Fragment.Call("TIMESTAMPDIFF", Fragment.Keyword("DAY"), from, to),
        SqlDialect.Oracle => Fragment.Extract(DateField.Day, to - from), // the interval's whole days
        SqlDialect.PostgreSql => Fragment.Cast(Fragment.Call("TRUNC", Fragment.Extract(DateField.Epoch, to - from) / 86400), SqlType.Integer),
        _ => Fragment.Emulated(Fragment.Cast(Fragment.Call("julianday", to) - Fragment.Call("julianday", from), SqlType.Integer), "dates are ISO text on SQLite: julianday reads them"),
    };
}

/// <summary>Declared chains: a [Query] that returns the chain instead of running it. Called in place (qualified, <c>Chains.Live(…)</c>), it is inlined by the generator wherever it is used: a root, a join's inner chain, a set operation's member.</summary>
internal static partial class Chains
{
    [Query]
    public static SqlQuery<Order> Live(SqlQuery<Order> orders, OrderStatus status) => orders.Where(o => o.Status == status && !o.IsDeleted);
}

/// <summary>
///     The SQL beyond one table and one row: the typed catalog (<c>Sql.*</c>), application functions, hand-written statements as a
///     root, navigation paths of any depth, collection predicates, CASE and COALESCE, DISTINCT and the set operations, composite
///     GROUP BY keys, explicit joins. Every chain is translated at compile time; the SQL is in the generated file.
/// </summary>
internal static class Algebra
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "algebra");
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var linesByOrder = seed.Lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        // The typed catalog: Sql.Year, Sql.Length, Sql.DateDiff… render per dialect (an emulation where a dialect has no native form: PRAG3012, information).
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var in2026 = await session.Query<Order>().CountAsync(o => Sql.Year(o.CreatedAt) == 2026 && Sql.Length(o.Number) == 10);
        var sinceEpoch = await session.Query<Order>().CountAsync((o, epoch) => Sql.DateDiff(DatePart.Day, epoch, o.CreatedAt) >= 100, epoch);
        checks.Check("typed catalog (Sql.Year, Sql.Length, Sql.DateDiff)", in2026 == seed.Orders.Count(o => o.CreatedAt.Year == 2026 && o.Number.Length == 10) && sinceEpoch == seed.Orders.Count(o => (int)Math.Floor((o.CreatedAt - epoch).TotalDays) >= 100));

        // The rest of the catalog: BETWEEN, a null-safe inequality, LIKE with an escape, NULLIF, GREATEST/LEAST, casts, a date as text (each with its dialect form; the emulations are said at the call site).
        var between = await session.Query<Order>().CountAsync((o, a) => Sql.Between(o.Total, a.low, a.high), (low: 100m, high: 200m));
        var distinct = await session.Query<Order>().CountAsync((o, n) => Sql.IsDistinctFrom(o.Number, n), "ORD-100000");
        var escaped = await session.Query<Order>().CountAsync((o, p) => Sql.Like(o.Number, p, "\\"), "ORD\\_1%");
        var shaped = await session.Query<Order>().Where((o, n) => o.Number == n, "ORD-100001").Select(o => new { Nulled = Sql.NullIf(o.Number, "ORD-100001"), High = Sql.Greatest(o.Total, 1_000_000m), Text = Sql.ToText(o.Status), Day = Sql.DateText(o.CreatedAt), Serial = Sql.ToInt32(Sql.Substring(o.Number, 4, 6)) }).FirstOrDefaultAsync();
        var second = seed.Orders.First(o => o.Number == "ORD-100001");
        checks.Check("catalog: Between, IsDistinctFrom, Like with escape, NullIf, Greatest, casts, DateText", between == seed.Orders.Count(o => o.Total >= 100m && o.Total <= 200m) && distinct == seed.Orders.Count - 1 && escaped == 0
            && shaped is { Nulled: null, High: 1_000_000m, Serial: 100001 } && shaped.Text == ((int)second.Status).ToString() && shaped.Day == second.CreatedAt.ToString("yyyy-MM-dd"));

        // Windows: ROW_NUMBER/RANK/LAG/SUM OVER a partition in a projection, COUNT(*) OVER () for a page with its total in one round trip; DistinctOn(key, order) for the
        // first row per key (DISTINCT ON on PostgreSQL, ROW_NUMBER() = 1 elsewhere): the "current per parent" pick, the filters before it choosing the candidates.
        var busy = seed.Customers[7].Id;
        var busyOrders = seed.Orders.Where(o => o.CustomerId == busy).OrderBy(o => o.Number, StringComparer.Ordinal).ToList();
        var paged = await session.Query<Order>().Where((o, id) => o.CustomerId == id, busy).OrderBy(o => o.Number).Skip(1).Take(2).Select(o => new { o.Number, N = Sql.RowNumber(o.CustomerId, o.Number), Previous = (decimal?)Sql.Lag(o.Total, 1, o.CustomerId, o.Number), Total = Sql.CountOver() }).ToListAsync();
        var latest = await session.Query<Order>().Where(o => !o.IsDeleted).DistinctOn(o => o.CustomerId, o => Sql.By(Sql.Desc(o.CreatedAt), o.Number)).CountAsync();
        checks.Check("windows: RowNumber, Lag, CountOver (page + total); DistinctOn (latest order per customer)",
            paged.Count == 2 && paged[0].N == 2 && paged[0].Previous == busyOrders[0].Total && paged.All(p => p.Total == busyOrders.Count) && latest == seed.Orders.Where(o => !o.IsDeleted).Select(o => o.CustomerId).Distinct().Count());

        // An application function: the same call in C#, its definition rendered where it is used; the method body itself only throws.
        var byFunction = await session.Query<Order>().CountAsync((o, epoch) => AppSql.DaysBetween(epoch, o.CreatedAt) >= 100, epoch);
        checks.Check("[SqlFunction] with typed fragments", byFunction == sinceEpoch);

        // A hand-written statement as the root: its holes are parameters, its identifiers follow the dialect's quoting, the chain applies on top.
        var minimum = 1000m;
        var raw = await session.Sql<Order>($"SELECT * FROM \"Orders\" WHERE \"Total\" > {minimum}").Where(o => !o.IsDeleted).OrderByDescending(o => o.Total).Take(3).ToListAsync();
        checks.Check("hand-written root (session.Sql<T>)", raw.Select(o => o.Id).SequenceEqual(seed.Orders.Where(o => o.Total > minimum && !o.IsDeleted).OrderByDescending(o => o.Total).Take(3).Select(o => o.Id)));

        // Paths of any depth (two joins), collection predicates as correlated subqueries (EXISTS, NOT EXISTS, COUNT), CASE from ?: and COALESCE from ??.
        var italianLines = await session.Query<OrderLine>().CountAsync(l => l.Order!.Customer!.Country == "IT");
        var bigLines = await session.Query<Order>().CountAsync(o => o.Lines.Any(l => l.Quantity > 8) && o.Lines.All(l => l.Quantity >= 1) && o.Lines.Count == Seed.LinesPerOrder);
        var labelled = await session.Query<Order>().OrderBy(o => o.Number).Take(3).Select(o => new { o.Number, State = o.IsDeleted ? "gone" : "live", Who = o.Customer!.Name ?? "nobody" }).ToListAsync();
        checks.Check("multi-hop paths, Any/All/Count, CASE, COALESCE", italianLines == seed.Lines.Count(l => customers[seed.Orders.First(o => o.Id == l.OrderId).CustomerId].Country == "IT")
            && bigLines == seed.Orders.Count(o => linesByOrder[o.Id].Any(l => l.Quantity > 8)) && labelled.Count == 3 && labelled.All(l => l.State is "gone" or "live" && l.Who.Length > 0));

        // DISTINCT, and the set operations: the members as a derived table, the result ordered and paged; UNION ALL keeps duplicates.
        var countries = await session.Query<Customer>().Select(c => c.Country).Distinct().CountAsync();
        var italianOrCancelled = await session.Query<Order>()
            .Where((o, country) => o.Customer!.Country == country, "IT")
            .Union(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled))
            .OrderBy(o => o.Number).Take(10).ToListAsync();
        var expectedUnion = seed.Orders.Where(o => customers[o.CustomerId].Country == "IT").Union(seed.Orders.Where(o => o.Status == OrderStatus.Cancelled)).OrderBy(o => o.Number, StringComparer.Ordinal).Take(10).Select(o => o.Number);
        var both = await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).Select(o => o.CustomerId).Intersect(session.Query<Order>().Where(o => o.Status == OrderStatus.Paid).Select(o => o.CustomerId)).CountAsync();
        checks.Check("Distinct, Union, Intersect", countries == seed.Customers.Select(c => c.Country).Distinct().Count() && italianOrCancelled.Select(o => o.Number).SequenceEqual(expectedUnion)
            && both == seed.Orders.Where(o => o.Status == OrderStatus.Open).Select(o => o.CustomerId).Intersect(seed.Orders.Where(o => o.Status == OrderStatus.Paid).Select(o => o.CustomerId)).Count());

        // A composite GROUP BY key (an anonymous object or a tuple): g.Key.Member is one column, g.Key the whole key rebuilt.
        var perCountryAndStatus = await session.Query<Order>().GroupBy(o => (o.Customer!.Country, o.Status)).Where((g, atLeast) => g.Count() >= atLeast, 3)
            .OrderBy(g => g.Key.Country).ThenBy(g => g.Key.Status).Select(g => new { Key = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).ToListAsync();
        var expectedGroups = seed.Orders.GroupBy(o => (customers[o.CustomerId].Country, o.Status)).Where(g => g.Count() >= 3).Select(g => (g.Key.Country, g.Key.Status, g.Count())).OrderBy(g => g.Country, StringComparer.Ordinal).ThenBy(g => g.Status);
        checks.Check("composite group key", perCountryAndStatus.Select(g => (g.Key.Country, g.Key.Status, g.Orders)).SequenceEqual(expectedGroups));

        // Explicit joins where no navigation exists: INNER JOIN on the table, LEFT JOIN on a filtered inner chain (a derived table), the result selector over both rows.
        var joined = await session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.Number).Take(5)
            .Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).ToListAsync();
        var leftJoined = await session.Query<Customer>()
            .LeftJoin(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number, Total = (decimal?)o.Total })
            .CountAsync();
        var openOrders = seed.Orders.Count(o => o.Status == OrderStatus.Open);
        var customersWithoutOpen = seed.Customers.Count(c => seed.Orders.All(o => o.CustomerId != c.Id || o.Status != OrderStatus.Open));
        checks.Check("Join and LeftJoin", joined.Select(j => j.Number).SequenceEqual(seed.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => o.Number))
            && joined.All(j => seed.Customers.Any(c => c.Name == j.Name)) && leftJoined == openOrders + customersWithoutOpen);

        // The joined row: the result selector names entities and values; more joins, Where, OrderBy, GroupBy and Select work on it, and nothing is
        // materialised in between (one statement, every member a column of it; the entities are hydrated only when the row itself is read).
        var orders = seed.Orders.ToDictionary(o => o.Id);
        var italianRows = await session.Query<Order>()
            .Join(session.Query<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { Order = o, Customer = c })
            .Join(session.Query<OrderLine>(), x => x.Order.Id, l => l.OrderId, (x, l) => new { x.Order, x.Customer, Line = l })
            .Where(x => x.Customer.Country == "IT" && x.Line.Quantity > 8 && !x.Order.IsDeleted)
            .OrderBy(x => x.Order.Number).ThenBy(x => x.Line.Sku)
            .Select(x => new { x.Order.Number, x.Customer.Name, x.Line.Sku }).ToListAsync();
        var expectedItalianLines = seed.Lines.Where(l => l.Quantity > 8 && customers[orders[l.OrderId].CustomerId].Country == "IT" && !orders[l.OrderId].IsDeleted)
            .Select(l => (orders[l.OrderId].Number, l.Sku)).OrderBy(r => r.Number, StringComparer.Ordinal).ThenBy(r => r.Sku, StringComparer.Ordinal);
        var perCountry = await session.Query<OrderLine>()
            .Join(session.Query<Order>(), l => l.OrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .GroupBy(x => x.Order.Customer!.Country)
            .Select(g => new { Country = g.Key, Units = g.Sum(x => x.Line.Quantity) }).ToListAsync();
        var hydrated = await session.Query<Customer>()
            .LeftJoin(session.Query<Order>().Where(o => o.Status == OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { Customer = c, Open = o })
            .Where(x => x.Customer.Country == "DE").ToListAsync();
        checks.Check("joined row: joins, Where, OrderBy, GroupBy, Select, hydration", italianRows.Select(r => (r.Number, r.Sku)).SequenceEqual(expectedItalianLines)
            && perCountry.OrderBy(p => p.Country, StringComparer.Ordinal).Select(p => (p.Country, p.Units)).SequenceEqual(seed.Lines.GroupBy(l => customers[orders[l.OrderId].CustomerId].Country).Select(g => (g.Key, g.Sum(l => l.Quantity))).OrderBy(g => g.Key, StringComparer.Ordinal))
            && hydrated.All(x => x.Customer.Country == "DE" && (x.Open is null || x.Open.Status == OrderStatus.Open)));

        // A declared chain, reused: as the root of a chain, and as the inner chain of a LEFT JOIN; its value travels in the chain's slots.
        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).ToList();
        var liveAbove = await Chains.Live(session.Query<Order>(), OrderStatus.Open).Where((o, min) => o.Total > min, 1000m).CountAsync();
        var customersWithLive = await session.Query<Customer>().LeftJoin(Chains.Live(session.Query<Order>(), OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number }).CountAsync();
        checks.Check("declared chain as root and as join inner", liveAbove == open.Count(o => o.Total > 1000m) && customersWithLive == open.Count + seed.Customers.Count(c => open.All(o => o.CustomerId != c.Id)));

        // Subqueries on any chain, written inside the lambda: EXISTS correlated to the row in a predicate, MIN and SUM(CASE) as projected values
        // (the "markers" of a cockpit row), IN (SELECT …) from a declared chain with its value from the argument form.
        var byCustomer = seed.Orders.ToLookup(o => o.CustomerId);
        var withHeavyLine = await session.Query<Order>().CountAsync(o => session.Query<OrderLine>().Any(l => l.OrderId == o.Id && l.Quantity > 8));
        var markers = await session.Query<Customer>().OrderBy(c => c.Name).Take(10).Select(c => new
        {
            c.Name,
            Earliest = (DateTimeOffset?)session.Query<Order>().Where(o => o.CustomerId == c.Id && !o.IsDeleted).Min(o => o.CreatedAt),
            Weight = (int?)session.Query<Order>().Where(o => o.CustomerId == c.Id).Sum(o => o.Status == OrderStatus.Open ? 10 : 1),
        }).ToListAsync();
        var withCancelled = await session.Query<Customer>().CountAsync((c, status) => Chains.Live(session.Query<Order>(), status).Select(o => o.CustomerId).Contains(c.Id), OrderStatus.Cancelled);
        var expectedMarkers = seed.Customers.OrderBy(c => c.Name, StringComparer.Ordinal).Take(10).Select(c => (c.Name, byCustomer[c.Id].Where(o => !o.IsDeleted).Select(o => (DateTimeOffset?)o.CreatedAt).Min(), byCustomer[c.Id].Any() ? (int?)byCustomer[c.Id].Sum(o => o.Status == OrderStatus.Open ? 10 : 1) : null));
        checks.Check("subqueries: EXISTS, MIN, SUM(CASE), IN (declared chain)", withHeavyLine == seed.Orders.Count(o => seed.Lines.Any(l => l.OrderId == o.Id && l.Quantity > 8))
            && markers.Select(m => (m.Name, m.Earliest, m.Weight)).SequenceEqual(expectedMarkers)
            && withCancelled == seed.Customers.Count(c => byCustomer[c.Id].Any(o => o.Status == OrderStatus.Cancelled && !o.IsDeleted)));

        // SelectMany: a row per element of a collection navigation (a JOIN on the foreign key); LINQ's GroupJoin + DefaultIfEmpty as the LEFT JOIN it is;
        // and "the last two open orders per customer": a chain correlated to the row with OrderBy and Take (LATERAL on PostgreSQL, APPLY on SQL Server, ROW_NUMBER on SQLite).
        var flattened = await session.Query<Order>().Where(o => !o.IsDeleted).SelectMany(o => o.Lines, (o, l) => new { o.Number, l.Sku, l.Quantity }).Where(x => x.Quantity > 8).CountAsync();
        var openByCustomer = seed.Orders.Where(o => o.Status == OrderStatus.Open).ToLookup(o => o.CustomerId);
        var groupJoined = await session.Query<Customer>()
            .GroupJoin(Chains.Live(session.Query<Order>(), OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Number = o!.Number }).CountAsync();
        var lastTwo = await session.Query<Customer>()
            .SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.Id && o.Status == OrderStatus.Open).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2), (c, o) => new { c.Name, o.Number })
            .ToListAsync();
        var expectedLastTwo = seed.Customers.SelectMany(c => openByCustomer[c.Id].OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(2).Select(o => (c.Name, o.Number))).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal);
        checks.Check("SelectMany: collection, GroupJoin+DefaultIfEmpty, the last two per customer", flattened == seed.Lines.Count(l => l.Quantity > 8 && !orders[l.OrderId].IsDeleted)
            && groupJoined == seed.Customers.Sum(c => Math.Max(1, openByCustomer[c.Id].Count(o => !o.IsDeleted)))
            && lastTwo.Select(r => (r.Name, r.Number)).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Number, StringComparer.Ordinal).SequenceEqual(expectedLastTwo));

        // Extended aggregates, as LINQ spells them on the group: filtered (a CASE inside, the form every dialect has), distinct, string.Join
        // (STRING_AGG, group_concat on SQLite); in the HAVING and the ordering too. A filtered aggregate over no matching row is NULL: declare it nullable.
        var report = await session.Query<Order>().GroupBy(o => o.CustomerId)
            .Where(g => g.Count(o => o.Status == OrderStatus.Open) > 2)
            .OrderByDescending(g => g.Where(o => !o.IsDeleted).Sum(o => o.Total)).Take(5)
            .Select(g => new
            {
                Customer = g.Key,
                Open = g.Count(o => o.Status == OrderStatus.Open),
                Live = (decimal?)g.Where(o => !o.IsDeleted).Sum(o => o.Total),
                Statuses = g.Select(o => o.Status).Distinct().Count(),
                Paid = string.Join(",", g.Where(o => o.Status == OrderStatus.Paid).Select(o => o.Number)),
            }).ToListAsync();
        var expectedReport = seed.Orders.GroupBy(o => o.CustomerId).Where(g => g.Count(o => o.Status == OrderStatus.Open) > 2).OrderByDescending(g => g.Where(o => !o.IsDeleted).Sum(o => o.Total)).Take(5)
            .Select(g => (g.Key, g.Count(o => o.Status == OrderStatus.Open), (decimal?)g.Where(o => !o.IsDeleted).Sum(o => o.Total), g.Select(o => o.Status).Distinct().Count(), g.Count(o => o.Status == OrderStatus.Paid))).ToList();
        checks.Check("extended aggregates: filtered, distinct, string.Join, in HAVING and ORDER BY", report.Select(r => (r.Customer, r.Open, r.Live, r.Statuses, (r.Paid ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length)).SequenceEqual(expectedReport));
        return checks.Failures;
    }
}
