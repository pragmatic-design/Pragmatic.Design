using System.Text.Json;
using Pragmatic.Persistence.Fuzz;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     The adapter <c>[GridAdapter]</c> generates refuses a null where a sort, a column or a global-filter column stands,
///     as the runtime adapters do (#845).
/// </summary>
/// <remarks>
///     Each was a <see cref="NullReferenceException" />, a 500, for a body the client controls: the generated loops read
///     <c>sort.Selector</c>, <c>sort.Field</c>, <c>filter.Value.Value</c> and <c>field.ToLowerInvariant()</c> on the
///     element itself. The adapter under test is <see cref="FuzzOrderGrid" />, the one the fuzz harness runs, generated
///     for both formats. Every request is deserialized from JSON, as an endpoint binds it.
/// </remarks>
public class ANullInAGeneratedGridRequestIsMalformedTests
{
    [Fact]
    public void A_null_devexpress_sort_descriptor_is_refused()
    {
        var options = JsonSerializer.Deserialize<DevExpressLoadOptions>("""{"sort":[null]}""", JsonSerializerOptions.Web)!;

        var act = () => FuzzOrderGrid.Apply(FuzzOrder.Rows(), options).ToList();

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.Malformed);
    }

    [Theory]
    [InlineData("""{"multiSortMeta":[null]}""")]
    [InlineData("""{"filters":{"customer":null}}""")]
    [InlineData("""{"globalFilter":"x","globalFilterFields":[null]}""")]
    public void A_null_primeng_sort_column_or_global_column_is_refused(string lazyEvent)
    {
        var options = JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(lazyEvent, JsonSerializerOptions.Web)!;

        var act = () => FuzzOrderGrid.Apply(FuzzOrder.Rows(), options).ToList();

        act.Should().Throw<GridFilterRejectedException>().Where(e => e.Rejection == GridFilterRejection.Malformed);
    }

    /// <summary>The control: a sort with an empty selector is passed over, as it was, so the refusal is not an off switch.</summary>
    [Fact]
    public void An_empty_devexpress_selector_is_still_passed_over()
    {
        var options = JsonSerializer.Deserialize<DevExpressLoadOptions>("""{"sort":[{"selector":""}]}""", JsonSerializerOptions.Web)!;

        FuzzOrderGrid.Apply(FuzzOrder.Rows(), options).ToList().Should().HaveCount(FuzzOrder.Rows().Count());
    }
}
