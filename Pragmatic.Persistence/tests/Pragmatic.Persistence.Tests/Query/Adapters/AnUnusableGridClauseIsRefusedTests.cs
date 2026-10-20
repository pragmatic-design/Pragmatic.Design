using System.Text.Json;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     A clause the runtime adapters cannot apply is refused, never dropped (#318).
/// </summary>
/// <remarks>
///     <para>
///         A filter restricts, so a clause left out answers with more rows than the client asked for, under a success
///         status. The DevExpress adapter dropped the <em>whole</em> filter when one clause of an <c>or</c> was
///         unusable: <c>[["number","=",3],"or",["nope","=",1]]</c> returned every row. Each case below returned rows
///         before this change; now each is a declared refusal the host answers with 400.
///     </para>
///     <para>
///         Every request is deserialized from JSON, as an endpoint binds it.
///     </para>
/// </remarks>
public class AnUnusableGridClauseIsRefusedTests
{
    private sealed class Order
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public string TenantId { get; set; } = "";
        public int? Rank { get; set; }
    }

    private static readonly IQueryable<Order> Orders =
        Enumerable.Range(0, 5)
            .Select(i => new Order { Number = i, Name = $"order {i}", TenantId = $"t{i}", Rank = i % 2 == 0 ? i : null })
            .ToArray().AsQueryable();

    /// <summary>
    ///     Against null, equality and inequality are comparisons on a column that can hold it; an ordering is not one,
    ///     and was applied as a lifted comparison that selects nothing.
    /// </summary>
    [Fact]
    public void A_devexpress_comparison_with_null_is_applied_for_equality_and_refused_for_an_ordering()
    {
        DevExpress("""["rank","=",null]""").Select(o => o.Number).Should().Equal(1, 3);
        DevExpress("""["rank","<>",null]""").Select(o => o.Number).Should().Equal(0, 2, 4);

        var act = () => DevExpress("""["rank",">",null]""");
        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    [Fact]
    public void A_devexpress_or_with_an_unknown_field_is_refused_not_the_whole_filter_dropped()
    {
        var act = () => DevExpress("""[["number","=",3],"or",["nope","=",1]]""");

        act.Should().Throw<GridFieldRejectedException>()
            .Where(e => e.Field == "nope" && e.Rejection == GridFieldRejection.Unknown);
    }

    [Fact]
    public void A_devexpress_clause_on_a_sensitive_column_is_refused_as_withheld()
    {
        var act = () => DevExpress("""["tenantId","=","t1"]""");

        act.Should().Throw<GridFieldRejectedException>().Where(e => e.Rejection == GridFieldRejection.Withheld);
    }

    [Theory]
    [InlineData("""["number","bogus",3]""", "bogus")]
    [InlineData("""["number","contains","3"]""", "contains")]
    public void A_devexpress_operator_the_field_does_not_support_is_refused(string filter, string op)
    {
        var act = () => DevExpress(filter);

        act.Should().Throw<GridFilterRejectedException>()
            .Where(e => e.Rejection == GridFilterRejection.UnsupportedOperator && e.Field == "number" && e.Operator == op);
    }

    [Theory]
    [InlineData("""["number","=","abc"]""")]
    [InlineData("""["number","=",[1,2]]""")]
    [InlineData("""["number","=",99999999999]""")]
    [InlineData("""["name","contains",null]""")]
    public void A_devexpress_value_the_field_cannot_be_compared_with_is_refused(string filter)
    {
        var act = () => DevExpress(filter);

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    [Theory]
    [InlineData("""[["number","=",3],"xor",["number","=",4]]""")]
    [InlineData("""[["number","=",3],"or"]""")]
    [InlineData("""[["number","=",3],"or",7]""")]
    [InlineData("""[[]]""")]
    [InlineData("""["!",3]""")]
    [InlineData("""["number","=",3,4]""")]
    public void A_devexpress_filter_that_is_not_a_clause_a_group_or_a_negation_is_refused(string filter)
    {
        var act = () => DevExpress(filter);

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.Malformed);
    }

    /// <summary>
    ///     A group of one clause is read as that clause. A filter builder that wraps a single condition in a group
    ///     got every row back: a list of one element was neither a clause nor a composite.
    /// </summary>
    [Fact]
    public void A_devexpress_group_of_one_clause_is_read_as_the_clause()
        => DevExpress("""[["number","=",3]]""").Should().ContainSingle().Which.Number.Should().Be(3);

    /// <summary>The control: a usable or-filter still selects what it names, or the refusals are an off switch.</summary>
    [Fact]
    public void A_usable_devexpress_or_still_selects_what_it_names()
        => DevExpress("""[["number","=",3],"or",["name","=","order 1"]]""").Select(o => o.Number).Should().Equal(1, 3);

    [Fact]
    public void A_primeng_filter_on_an_unknown_field_is_refused()
    {
        var act = () => PrimeNG("""{"filters":{"nope":{"value":1,"matchMode":"equals"}}}""");

        act.Should().Throw<GridFieldRejectedException>().Where(e => e.Field == "nope");
    }

    /// <summary>An unknown match mode was read as equals: <c>dateBefore</c> answered with the rows on that date.</summary>
    [Fact]
    public void A_primeng_match_mode_the_adapter_does_not_know_is_refused_not_read_as_equals()
    {
        var act = () => PrimeNG("""{"filters":{"number":{"value":3,"matchMode":"dateBefore"}}}""");

        act.Should().Throw<GridFilterRejectedException>()
            .Where(e => e.Rejection == GridFilterRejection.UnsupportedOperator && e.Operator == "dateBefore");
    }

    [Theory]
    [InlineData("""{"value":"abc","matchMode":"equals"}""")]
    [InlineData("""{"value":[],"matchMode":"in"}""")]
    [InlineData("""{"value":3,"matchMode":"in"}""")]
    [InlineData("""{"value":[1,2,3],"matchMode":"between"}""")]
    [InlineData("""{"value":["a","b"],"matchMode":"in"}""")]
    public void A_primeng_value_the_field_cannot_be_compared_with_is_refused(string meta)
    {
        var act = () => PrimeNG("""{"filters":{"number":""" + meta + "}}");

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    [Fact]
    public void A_primeng_global_filter_naming_an_unknown_column_is_refused()
    {
        var act = () => PrimeNG("""{"globalFilter":"order","globalFilterFields":["name","nope"]}""");

        act.Should().Throw<GridFieldRejectedException>().Where(e => e.Field == "nope");
    }

    /// <summary>
    ///     A global filter applies to the text columns it names; one that names none of them restricts nothing, and
    ///     answered with every row.
    /// </summary>
    [Fact]
    public void A_primeng_global_filter_with_no_text_column_is_refused()
    {
        var act = () => PrimeNG("""{"globalFilter":"order","globalFilterFields":["number"]}""");

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.UnusableValue);
    }

    /// <summary>The controls: a column filter and a global filter over a text column still apply.</summary>
    [Fact]
    public void A_usable_primeng_request_still_applies()
    {
        PrimeNG("""{"filters":{"number":{"value":[1,3],"matchMode":"in"}}}""").Select(o => o.Number).Should().Equal(1, 3);
        PrimeNG("""{"globalFilter":"order 4","globalFilterFields":["name","number"]}""").Should().ContainSingle();
    }

    private static List<Order> DevExpress(string filter)
    {
        var options = JsonSerializer.Deserialize<DevExpressLoadOptions>($"{{\"filter\":{filter}}}", JsonSerializerOptions.Web)!;
        return PersistenceQuery.For<Order>().FromDevExpress(options).Build(Orders).ToList();
    }

    private static List<Order> PrimeNG(string lazyEvent)
    {
        var options = JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(lazyEvent, JsonSerializerOptions.Web)!;
        return PersistenceQuery.For<Order>().FromPrimeNG(options).Build(Orders).ToList();
    }
}
