using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     A grid filter is refused past <see cref="GridFilterTooLargeException.MaxTerms" /> terms (#310).
/// </summary>
/// <remarks>
///     <para>
///         The filter a client sends becomes a chain as deep as it has terms, and what reads the chain walks
///         it recursively. A DevExpress filter of 10,000 terms, 250 KB of JSON, ended the process with a stack
///         overflow through either adapter; EF Core's translation overflowed already between 1,000 and 1,250.
///         The refusal has to come before the chain is built, which is what these tests pin: at the bound the
///         filter is applied, one past it the adapter throws.
///     </para>
///     <para>
///         Every DevExpress request here is deserialized from JSON, as an endpoint binds it, so its elements
///         are <c>JsonElement</c>s and not the CLR lists a hand-written test would pass.
///     </para>
/// </remarks>
public class AGridFilterHasAWidthTests
{
    private sealed class Order
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
    }

    private static readonly IQueryable<Order> Orders =
        Enumerable.Range(0, 300).Select(i => new Order { Number = i, Name = $"order {i}" }).ToArray().AsQueryable();

    [Fact]
    public void A_devexpress_filter_at_the_bound_is_applied()
    {
        var rows = PersistenceQuery.For<Order>()
            .FromDevExpress(DevExpress(Ors(GridFilterTooLargeException.MaxTerms)))
            .Build(Orders)
            .ToList();

        rows.Should().HaveCount(GridFilterTooLargeException.MaxTerms, "each term selects one order");
    }

    [Fact]
    public void A_devexpress_filter_one_term_past_the_bound_is_refused()
    {
        var act = () => PersistenceQuery.For<Order>().FromDevExpress(DevExpress(Ors(GridFilterTooLargeException.MaxTerms + 1)));

        act.Should().Throw<GridFilterTooLargeException>();
    }

    /// <summary>The terms are counted across groups, not per group: depth adds up the same way width does.</summary>
    [Fact]
    public void A_devexpress_filter_is_counted_across_its_groups()
    {
        var groups = string.Join(",\"and\",", Enumerable.Range(0, 3).Select(_ => "[" + Ors(100) + "]"));

        var act = () => PersistenceQuery.For<Order>().FromDevExpress(DevExpress(groups));

        act.Should().Throw<GridFilterTooLargeException>().Where(e => e.Terms > GridFilterTooLargeException.MaxTerms);
    }

    /// <summary>
    ///     A value that does not convert to the column's type is refused as an unusable value, not an exception out
    ///     of the adapter.
    /// </summary>
    /// <remarks>
    ///     Each of these escaped as an unhandled exception, a 500 (#310), and then was dropped, which answered with
    ///     every row (#318).
    /// </remarks>
    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("[1,2]")]
    [InlineData("99999999999")]
    public void A_devexpress_value_that_does_not_convert_is_refused(string value)
    {
        var act = () => PersistenceQuery.For<Order>()
            .FromDevExpress(DevExpress($"\"number\",\"=\",{value}"))
            .Build(Orders)
            .ToList();

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    /// <summary>
    ///     A string match against <c>null</c> is refused as an unusable value.
    /// </summary>
    /// <remarks>
    ///     Found by <see cref="GridAdapterPropertyTests" />: the adapter built <c>e.Name.StartsWith(null)</c>, which
    ///     throws <see cref="ArgumentNullException" /> on the first row it is evaluated against.
    /// </remarks>
    [Theory]
    [InlineData("contains")]
    [InlineData("startswith")]
    [InlineData("endswith")]
    public void A_devexpress_string_match_against_null_is_refused(string op)
    {
        var act = () => PersistenceQuery.For<Order>()
            .FromDevExpress(DevExpress($"\"name\",\"{op}\",null"))
            .Build(Orders)
            .ToList();

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    /// <summary>
    ///     A string that is not UTF-8 is refused where it stands: as a value, an unusable one; as a field name, a
    ///     connective or a group, a filter that is not one.
    /// </summary>
    /// <remarks>
    ///     Found by the fuzz harness: an element of <c>Filter</c> binds as a <c>JsonElement</c> whose text is not
    ///     validated until it is read, and reading it threw <see cref="InvalidOperationException" />, a 500 (#310).
    ///     Read as <c>null</c> after that, <c>["name","&lt;&gt;",…]</c> became <c>name != null</c>, every row (#318).
    /// </remarks>
    [Theory]
    [InlineData("[\"\u0001name\",\"=\",\"Ada\"]", GridFilterRejection.Malformed)]
    [InlineData("[\"name\",\"=\",\"\u0001Ada\"]", GridFilterRejection.UnusableValue)]
    [InlineData("[\"name\",\"<>\",\"\u0001Ada\"]", GridFilterRejection.UnusableValue)]
    [InlineData("[[\"name\",\"=\",\"Ada\"],\"\u0001or\",[\"number\",\"=\",1]]", GridFilterRejection.Malformed)]
    [InlineData("[[\"name\",\"=\",\"Ada\"],\"and\",{\"\u0001selector\":\"name\"}]", GridFilterRejection.Malformed)]
    [InlineData("[\"name\",\"=\",{\"\u0001key\":1}]", GridFilterRejection.UnusableValue)]
    public void A_devexpress_string_that_is_not_utf8_is_refused(string filter, GridFilterRejection rejection)
    {
        // \u0001 marks where the invalid byte goes: 0x9A, a continuation byte with nothing before it.
        var body = Encoding.UTF8.GetBytes($"{{\"filter\":{filter}}}").Select(b => b == 0x01 ? (byte)0x9A : b).ToArray();
        var options = JsonSerializer.Deserialize<DevExpressLoadOptions>(body, JsonSerializerOptions.Web)!;

        var act = () => PersistenceQuery.For<Order>().FromDevExpress(options).Build(Orders).ToList();

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == rejection);
    }

    [Fact]
    public void A_primeng_in_with_more_values_than_the_bound_is_refused()
    {
        var act = () => PersistenceQuery.For<Order>().FromPrimeNG(new PrimeNGLazyLoadEvent
        {
            Filters = new Dictionary<string, PrimeNGFilterMetadata>
            {
                ["number"] = new() { MatchMode = "in", Value = Enumerable.Range(0, GridFilterTooLargeException.MaxTerms + 1).ToArray() },
            },
        });

        act.Should().Throw<GridFilterTooLargeException>();
    }

    [Fact]
    public void A_primeng_global_filter_over_more_columns_than_the_bound_is_refused()
    {
        var act = () => PersistenceQuery.For<Order>().FromPrimeNG(new PrimeNGLazyLoadEvent
        {
            GlobalFilter = "order",
            GlobalFilterFields = [.. Enumerable.Repeat("name", GridFilterTooLargeException.MaxTerms + 1)],
        });

        act.Should().Throw<GridFilterTooLargeException>();
    }

    [Fact]
    public void A_primeng_request_with_more_column_filters_than_the_bound_is_refused()
    {
        var act = () => PersistenceQuery.For<Order>().FromPrimeNG(new PrimeNGLazyLoadEvent
        {
            Filters = Enumerable.Range(0, GridFilterTooLargeException.MaxTerms + 1)
                .ToDictionary(i => $"column{i}", _ => new PrimeNGFilterMetadata { Value = "x", MatchMode = "equals" }),
        });

        act.Should().Throw<GridFilterTooLargeException>();
    }

    /// <summary>A sort descriptor nests the query like a clause does, so it is counted with the filter.</summary>
    [Fact]
    public void A_devexpress_request_whose_sorts_and_clauses_pass_the_bound_together_is_refused()
    {
        var options = DevExpress(Ors(200));
        var act = () => PersistenceQuery.For<Order>().FromDevExpress(new DevExpressLoadOptions
        {
            Filter = options.Filter,
            Sort = [.. Enumerable.Range(0, 57).Select(_ => new DevExpressSortDescriptor { Selector = "name" })],
        });

        act.Should().Throw<GridFilterTooLargeException>();
    }

    [Fact]
    public void A_primeng_request_with_more_sorts_than_the_bound_is_refused()
    {
        var act = () => PersistenceQuery.For<Order>().FromPrimeNG(new PrimeNGLazyLoadEvent
        {
            MultiSortMeta = [.. Enumerable.Range(0, GridFilterTooLargeException.MaxTerms + 1).Select(_ => new PrimeNGSortMeta { Field = "name" })],
        });

        act.Should().Throw<GridFilterTooLargeException>();
    }

    private static string Ors(int terms)
        => string.Join(",\"or\",", Enumerable.Range(0, terms).Select(i => $"[\"number\",\"=\",{i}]"));

    private static DevExpressLoadOptions DevExpress(string filter)
        => JsonSerializer.Deserialize<DevExpressLoadOptions>($"{{\"filter\":[{filter}]}}", JsonSerializerOptions.Web)!;
}
