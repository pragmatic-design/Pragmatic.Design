using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Persistence.Query.Adapters;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     The runtime grid adapters read load options a client sends (#158, #310). For any request they have two
///     outcomes: a query that runs, or a <see cref="GridFilterTooLargeException" /> past the width bound. Every
///     request here is JSON deserialized as an endpoint binds it.
/// </summary>
public class GridAdapterPropertyTests
{
    private sealed class Order
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
    }

    private static readonly Order[] Orders = [.. Enumerable.Range(0, 20).Select(i => new Order { Number = i, Name = $"order {i}" })];

    private static readonly Gen<string> Clauses = Gen.Elements(
        "[\"number\",\"=\",3]", "[\"number\",\">\",10]", "[\"name\",\"contains\",\"1\"]", "[\"name\",\"startswith\",\"order\"]",
        "[\"number\",\"<>\",0]");

    // A clause or, now and then, a small group of them or a negated clause: what a filter builder sends. A group
    // has two clauses at least: the runtime parser reads a group of one as nothing and drops the whole filter,
    // which is #318's to decide, not this property's.
    private static readonly Gen<(string Json, int Clauses)> Elements = Gen.Frequency(
        (9, Clauses.Select(c => (c, 1))),
        (1, from width in Gen.Choose(2, 5)
            from items in Clauses.ListOf(width)
            from connective in Gen.Elements("and", "or")
            select ("[" + string.Join($",\"{connective}\",", items) + "]", width)),
        (1, Clauses.Select(c => ($"[\"!\",{c}]", 1))));

    // Up to past the bound on either side: 1 to 300 elements, some of them groups, and up to 40 sorts.
    private static readonly Gen<(string Json, int Terms)> ValidRequests =
        from width in Gen.Choose(1, 300)
        from elements in Elements.ListOf(width)
        from connective in Gen.Elements("and", "or")
        from sorts in Gen.Choose(0, 40)
        let filter = width == 1 ? elements[0].Json : "[" + string.Join($",\"{connective}\",", elements.Select(e => e.Json)) + "]"
        let sort = string.Join(",", Enumerable.Repeat("{\"selector\":\"number\"}", sorts))
        select ($"{{\"filter\":{filter},\"sort\":[{sort}]}}", elements.Sum(e => e.Clauses) + sorts);

    // What the format allows a client to put anywhere: unknown fields and operators, values of every JSON kind,
    // connectives that are not one, groups nested in groups. An array at the top, since the binding refuses
    // anything else before the adapter is reached.
    private static readonly Gen<string> Anything = Gen.Sized(size => AnyArray(Math.Min(size, 6)));

    [Fact]
    public void A_devexpress_request_is_refused_exactly_when_it_passes_the_bound()
        => Prop.ForAll(ValidRequests.ToArbitrary(), request =>
        {
            var options = JsonSerializer.Deserialize<DevExpressLoadOptions>(request.Json, JsonSerializerOptions.Web)!;
            try
            {
                PersistenceQuery.For<Order>().FromDevExpress(options).Build(Orders.AsQueryable()).ToList();
                return request.Terms <= GridFilterTooLargeException.MaxTerms;
            }
            catch (GridFilterTooLargeException)
            {
                return request.Terms > GridFilterTooLargeException.MaxTerms;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(200));

    [Fact]
    public void Any_devexpress_filter_gives_a_query_that_runs_or_the_refusal()
        => Prop.ForAll(Anything.ToArbitrary(), filter =>
        {
            var options = JsonSerializer.Deserialize<DevExpressLoadOptions>($"{{\"filter\":{filter}}}", JsonSerializerOptions.Web)!;
            try
            {
                PersistenceQuery.For<Order>().FromDevExpress(options).Build(Orders.AsQueryable()).ToList();
            }
            catch (GridFilterTooLargeException)
            {
            }

            return true;
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(500));

    [Fact]
    public void Any_primeng_request_gives_a_query_that_runs_or_the_refusal()
    {
        var filters =
            from count in Gen.Choose(0, 6)
            from entries in (from field in Gen.Elements("number", "name", "nope", "Number")
                             from value in AnyValue
                             from mode in Gen.Elements("equals", "notEquals", "lt", "gte", "contains", "in", "between", "bogus")
                             select $"\"{field}\":{{\"value\":{value},\"matchMode\":\"{mode}\"}}").ListOf(count)
            select "{" + string.Join(",", entries.Distinct()) + "}";
        var requests =
            from filter in filters
            from global in Gen.Elements("null", "\"\"", "\"order\"")
            from fields in Gen.Elements("null", "[]", "[\"name\",\"number\",\"nope\"]")
            from sortField in Gen.Elements("null", "\"number\"", "\"nope\"")
            select $"{{\"filters\":{filter},\"globalFilter\":{global},\"globalFilterFields\":{fields},\"sortField\":{sortField},\"first\":0,\"rows\":10}}";

        Prop.ForAll(requests.ToArbitrary(), json =>
        {
            var lazyEvent = JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(json, JsonSerializerOptions.Web)!;
            try
            {
                PersistenceQuery.For<Order>().FromPrimeNG(lazyEvent).Build(Orders.AsQueryable()).ToList();
            }
            catch (GridFilterTooLargeException)
            {
            }

            return true;
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(300));
    }

    private static readonly Gen<string> AnyValue = Gen.Elements(
        "null", "0", "-1", "3", "99999999999", "1.5", "1e400", "\"abc\"", "\"3\"", "\"\"", "true", "[]", "[1,2]", "[\"a\"]", "{}",
        "{\"x\":1}");

    private static Gen<string> AnyNode(int depth) => Gen.Frequency((6, AnyArray(depth)), (1, AnyValue));

    private static Gen<string> AnyArray(int depth)
    {
        var leaf =
            from field in Gen.Elements("\"number\"", "\"name\"", "\"nope\"", "\"!\"", "\"and\"", "1", "null", "[]")
            from op in Gen.Elements("\"=\"", "\"<>\"", "\">\"", "\"contains\"", "\"startswith\"", "\"bogus\"", "null", "3")
            from value in AnyValue
            select $"[{field},{op},{value}]";

        if (depth <= 0)
            return leaf;

        var group =
            from items in AnyNode(depth - 1).ListOf()
            from connective in Gen.Elements("\"and\"", "\"or\"", "\"xor\"", "1", "null")
            select "[" + string.Join($",{connective},", items) + "]";
        var negated = AnyNode(depth - 1).Select(inner => $"[\"!\",{inner}]");

        return Gen.Frequency((3, leaf), (2, group), (1, negated));
    }
}
