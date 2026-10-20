using System.Text.Json;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     A PrimeNG request bound from JSON, as an endpoint binds it (#317). <see cref="PrimeNGFilterMetadata.Value" /> is
///     an <c>object?</c>, so System.Text.Json gives it a <see cref="JsonElement" />, which the adapter never unwrapped:
///     every column filter was skipped, and <c>in</c>/<c>between</c> did not apply even with CLR values, because the
///     operand was built from the whole array before the match mode was read. A grid showed every row whatever the
///     user filtered, with a success status.
/// </summary>
public class APrimeNGFilterIsAppliedTests
{
    private enum Kind
    {
        City = 1,
        Town = 2,
    }

    private sealed class Item
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public Kind Kind { get; set; }
    }

    private static readonly Item[] Items =
    [
        new() { Name = "Rome", Age = 30, Kind = Kind.City },
        new() { Name = "Milan", Age = 40, Kind = Kind.City },
        new() { Name = "Turin", Age = 50, Kind = Kind.Town },
    ];

    [Theory]
    [InlineData("""{"filters":{"name":{"value":"Rome","matchMode":"equals"}}}""", "Rome")]
    [InlineData("""{"filters":{"name":{"value":"o","matchMode":"contains"}}}""", "Rome")]
    [InlineData("""{"filters":{"name":{"value":"Mi","matchMode":"startsWith"}}}""", "Milan")]
    [InlineData("""{"filters":{"age":{"value":40,"matchMode":"equals"}}}""", "Milan")]
    [InlineData("""{"filters":{"age":{"value":40,"matchMode":"gte"}}}""", "Milan,Turin")]
    [InlineData("""{"filters":{"age":{"value":[30,40],"matchMode":"in"}}}""", "Rome,Milan")]
    [InlineData("""{"filters":{"age":{"value":[35,45],"matchMode":"between"}}}""", "Milan")]
    [InlineData("""{"filters":{"kind":{"value":"town","matchMode":"equals"}}}""", "Turin")]
    [InlineData("""{"filters":{"kind":{"value":["City"],"matchMode":"in"}}}""", "Rome,Milan")]
    public void A_filter_bound_from_json_selects_its_rows(string json, string expected)
        => Names(Apply(JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(json, JsonSerializerOptions.Web)!))
            .Should().Equal(expected.Split(','));

    [Fact]
    public void An_in_filter_with_clr_values_selects_its_rows()
        => Names(Apply(Column("Age", "in", new[] { 30, 50 }))).Should().Equal("Rome", "Turin");

    [Fact]
    public void A_between_filter_with_clr_values_selects_its_rows()
        => Names(Apply(Column("Age", "between", new List<object> { 25, 45 }))).Should().Equal("Rome", "Milan");

    // An unusable clause is refused (#318): dropped, it left the other clauses to answer with more rows than the
    // grid asked for, under a success status.
    [Theory]
    [InlineData("""{"filters":{"age":{"value":"abc","matchMode":"equals"},"name":{"value":"Rome","matchMode":"equals"}}}""", GridFilterRejection.UnusableValue)]
    [InlineData("""{"filters":{"age":{"value":[30,"abc"],"matchMode":"in"},"name":{"value":"Rome","matchMode":"equals"}}}""", GridFilterRejection.UnusableValue)]
    [InlineData("""{"filters":{"age":{"value":[30],"matchMode":"between"},"name":{"value":"Rome","matchMode":"equals"}}}""", GridFilterRejection.UnusableValue)]
    [InlineData("""{"filters":{"name":{"value":5,"matchMode":"lt"},"age":{"value":30,"matchMode":"equals"}}}""", GridFilterRejection.UnsupportedOperator)]
    public void An_unusable_clause_is_refused(string json, GridFilterRejection rejection)
    {
        var act = () => Apply(JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(json, JsonSerializerOptions.Web)!);

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == rejection);
    }

    private static PrimeNGLazyLoadEvent Column(string field, string matchMode, object value) => new()
    {
        Filters = new Dictionary<string, PrimeNGFilterMetadata> { [field] = new() { Value = value, MatchMode = matchMode } },
    };

    private static List<Item> Apply(PrimeNGLazyLoadEvent request)
        => PersistenceQuery.For<Item>().FromPrimeNG(request).Build(Items.AsQueryable()).ToList();

    private static string[] Names(List<Item> items) => [.. items.Select(item => item.Name)];
}
