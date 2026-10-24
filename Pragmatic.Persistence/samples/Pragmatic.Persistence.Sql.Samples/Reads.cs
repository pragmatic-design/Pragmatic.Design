using Pragmatic.Persistence.Workload;
using Pragmatic.Persistence.Sql.Attributes;
using Pragmatic.Persistence.Sql.Shapes;
using Pragmatic.Persistence.Sql.Execution;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     The typed chain: LINQ on <c>SqlQuery&lt;T&gt;</c>, translated at compile time by the generator into one plan per terminal call
///     site (the SQL per dialect, the parameters' types, the reader), run through a command prepared once per session. The
///     lambdas are plain delegates the generator reads as source: no expression tree is built, nothing runs in memory.
/// </summary>
internal static class Reads
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "reads");
        var customerId = seed.BusyCustomer.Id;
        var orderId = seed.Orders[4321].Id;

        // Constants in a predicate (an enum, a bool, && and !): the values become static fields of the plan, bound without boxing per call.
        var open = await session.Query<Order>().Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).CountAsync();
        checks.Check("constants in the predicate", open == seed.Orders.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted));

        // The caller's values go in the two-parameter form: the argument sits in a slot of the chain and its members bind straight into
        // the prepared command. A lambda that captures a local instead is reported at compile time (PRAG3002) with this remedy.
        var byId = await session.Query<Order>().FirstOrDefaultAsync((o, id) => o.Id == id, orderId);
        checks.Check("argument form", byId is not null && byId.Number == seed.Orders[4321].Number);

        // Several values travel as a tuple; a method on the value (AddDays) runs in .NET before the bind.
        var since = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var recent = await session.Query<Order>().Where((o, a) => o.CustomerId == a.CustomerId && o.CreatedAt >= a.Since.AddDays(-1), (CustomerId: customerId, Since: since)).CountAsync();
        checks.Check("tuple argument", recent == seed.Orders.Count(o => o.CustomerId == customerId && o.CreatedAt >= since.AddDays(-1)));

        // Strings: Contains/StartsWith/EndsWith are LIKE (the pattern built from the value), ToLower/ToUpper, IsNullOrEmpty; an IN list from a collection.
        var text = "1004";
        var like = await session.Query<Order>().CountAsync((o, text) => o.Number.Contains(text), text);
        var countries = new[] { "IT", "DE" };
        var inList = await session.Query<Customer>().CountAsync((c, countries) => countries.Contains(c.Country), countries);
        var lower = await session.Query<Customer>().CountAsync(c => c.Name.ToLower().StartsWith("customer 00") || string.IsNullOrEmpty(c.Email));
        checks.Check("LIKE, IN, ToLower, IsNullOrEmpty", like == seed.Orders.Count(o => o.Number.Contains(text, StringComparison.Ordinal)) && inList == seed.Customers.Count(c => countries.Contains(c.Country)) && lower == 10);

        // A navigation in the predicate is a JOIN: INNER when the foreign key is not nullable (CustomerId), LEFT when it is, unless the predicate needs the row.
        var italian = await session.Query<Order>().CountAsync(o => o.Customer!.Country == "IT" && !o.IsDeleted);
        var italianCustomers = seed.Customers.Where(c => c.Country == "IT").Select(c => c.Id).ToHashSet();
        checks.Check("navigation in the predicate (JOIN)", italian == seed.Orders.Count(o => italianCustomers.Contains(o.CustomerId) && !o.IsDeleted));

        // Ordering and paging: OrderBy/ThenBy and Skip/Take become ORDER BY and the dialect's paging (LIMIT/OFFSET, OFFSET/FETCH).
        var page = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number).Skip(10).Take(5).ToListAsync();
        var expectedPage = seed.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number, StringComparer.Ordinal).Skip(10).Take(5).Select(o => o.Id);
        checks.Check("order by, skip, take", page.Select(o => o.Id).SequenceEqual(expectedPage));

        // Keyset pagination: After(cursor) continues the ordering from the last row's keys (a tuple, one item per key) instead of
        // counting rows away with OFFSET; a row-value comparison where the dialect has it, the expanded form elsewhere. The
        // ordering must end with the entity's key, or the generator warns (PRAG3009).
        var ordered = session.Query<Order>().Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(20).ToListAsync();
        var next = await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(20).ToListAsync();
        var byOffset = await ordered.Skip(20).Take(20).ToListAsync();
        checks.Check("keyset pagination", next.Select(o => o.Id).SequenceEqual(byOffset.Select(o => o.Id)));

        // Both ways from an API: the page's edges signed into opaque tokens (SqlCursorSigner: the keys' values, HMAC-SHA256, Base64Url) and
        // handed to the client as next/prev; a token sent back decodes into the cursor After or Before take. Before(cursor) is the page just
        // before the cursor, in the chain's order (the statement compares and orders the other way, the plan turns the rows back).
        var signer = new SqlCursorSigner(Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"), "orders");
        var nextToken = signer.Encode((first[^1].CreatedAt, first[^1].Id));
        var page2 = await ordered.After(signer.Decode<DateTimeOffset, Guid>(nextToken)).Take(20).ToListAsync();
        var prevToken = signer.Encode((page2[0].CreatedAt, page2[0].Id));
        var page1 = await ordered.Before(signer.Decode<DateTimeOffset, Guid>(prevToken)).Take(20).ToListAsync();
        var refused = false;
        try
        {
            _ = signer.Decode<DateTimeOffset, Guid>(prevToken[..^4] + "AAAA");
        }
        catch (SqlCursorException)
        {
            refused = true;
        }

        checks.Check("Before(cursor), next/prev as signed tokens", page2.Select(o => o.Id).SequenceEqual(next.Select(o => o.Id)) && page1.Select(o => o.Id).SequenceEqual(first.Select(o => o.Id)) && refused);

        // Client shaping, as a DevExtreme grid asks it (filter array, sorts, skip/take, the total; strings case-insensitive): the compiled chain
        // decides the rows and the members (a projection), the client how to look at them; one command, the count included. A field the rows
        // do not expose is refused (SqlShapeException), a narrowed select comes back as dictionaries.
        var grid = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme(
            "[[\"Total\", \">\", 1000], \"and\", [\"CustomerName\", \"endswith\", \"7\"]]", "[{\"selector\": \"Total\", \"desc\": true}, \"Number\"]", skip: 2, take: 5, requireTotalCount: true));
        var gridExpected = seed.Orders.Where(o => !o.IsDeleted && o.Total > 1000m && seed.Customers.First(c => c.Id == o.CustomerId).Name.EndsWith('7')).OrderByDescending(o => o.Total).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
        var narrowed = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme("[\"Total\", \">\", 1000]", "[\"Number\"]", take: 2, select: ["Id", "Number"]));
        checks.Check("client shaping (a DevExtreme request) over a projection", grid.TotalCount == gridExpected.Count && grid.Rows.Select(r => r.Id).SequenceEqual(gridExpected.Skip(2).Take(5).Select(o => o.Id)) && narrowed.Narrowed!.Count == 2 && narrowed.Narrowed.All(r => r.Keys.SequenceEqual(["Id", "Number"])));

        // Projections: a column, an anonymous object, a DTO with an object initializer; and Project<TDto>() with no lambda at all (the
        // DTO's properties name the columns, CustomerName reaches the Customer navigation by convention), into a class or a record struct.
        var numbers = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Select(o => o.Number).ToListAsync();
        var pairs = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Select(o => new { o.Number, Who = o.Customer!.Name }).ToListAsync();
        var dtos = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name }).ToListAsync();
        var declared = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Project<OrderRowDto>().ToListAsync();
        var structs = await session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).OrderBy(o => o.Number).Take(3).Project<OrderRowStruct>().ToListAsync();
        var expectedNumbers = seed.Orders.Where(o => o.CustomerId == customerId).OrderBy(o => o.Number, StringComparer.Ordinal).Take(3).Select(o => o.Number).ToList();
        checks.Check("projections", numbers.SequenceEqual(expectedNumbers) && pairs.Select(p => p.Number).SequenceEqual(expectedNumbers) && pairs.All(p => p.Who == seed.BusyCustomer.Name)
            && dtos.Select(d => d.Number).SequenceEqual(expectedNumbers) && declared.Select(d => d.Number).SequenceEqual(expectedNumbers) && structs.Select(s => s.Number).SequenceEqual(expectedNumbers));

        // Scalars and aggregates: Count/Any with or without a predicate, Sum/Min/Max/Average of a column, First/Single, a dictionary keyed in memory.
        var mine = session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId);
        var expectedMine = seed.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).ToList();
        var sum = await mine.SumAsync(o => o.Total);
        var min = await mine.MinAsync(o => o.Total);
        var max = await mine.MaxAsync(o => o.Total);
        var average = await mine.AverageAsync(o => o.Total);
        var any = await mine.AnyAsync(o => o.Status == OrderStatus.Cancelled);
        var single = await session.Query<Order>().Where((o, id) => o.Id == id, orderId).SingleAsync();
        var dictionary = await mine.OrderBy(o => o.Number).Take(5).ToDictionaryAsync(o => o.Id);
        checks.Check("aggregates and scalars", sum == expectedMine.Sum(o => o.Total) && min == expectedMine.Min(o => o.Total) && max == expectedMine.Max(o => o.Total) && Math.Abs(average - (double)expectedMine.Average(o => o.Total)) < 0.01
            && any == expectedMine.Any(o => o.Status == OrderStatus.Cancelled) && single.Id == orderId && dictionary.Count == 5);

        // GroupBy: the key and the aggregates in one GROUP BY statement; a Where on the groups is HAVING, an OrderBy on an aggregate orders the groups.
        var atLeast = 100;
        var busiest = await session.Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Where((g, atLeast) => g.Count() > atLeast, atLeast)
            .OrderByDescending(g => g.Sum(o => o.Total)).ThenBy(g => g.Key).Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).Take(5).ToListAsync();
        var expectedBusiest = seed.Orders.Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Where(g => g.Count() > atLeast).OrderByDescending(g => g.Sum(o => o.Total)).ThenBy(g => g.Key).Take(5).Select(g => g.Key);
        checks.Check("group by, having, order by aggregate", busiest.Select(b => b.Customer).SequenceEqual(expectedBusiest));

        // Streaming: the rows as the reader yields them, no list, one row in memory at a time (not with a collection Include).
        var streamed = 0;
        await foreach (var order in mine.OrderBy(o => o.Number).Take(10).AsAsyncEnumerable())
            streamed += order.Number.Length > 0 ? 1 : 0;
        checks.Check("streaming", streamed == 10);

        // Inspection: the SQL of a chain on the session's dialect, and the command as it would run, values bound.
        var sql = mine.OrderBy(o => o.Number).Take(10).ToSql();
        using var command = mine.OrderBy(o => o.Number).Take(10).ToCommand();
        checks.Check("ToSql and ToCommand", sql.StartsWith("SELECT ", StringComparison.Ordinal) && command.CommandText == sql && command.Parameters.Count == 3);
        return checks.Failures;
    }
}
