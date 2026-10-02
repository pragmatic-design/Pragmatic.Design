using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     How far validation reaches, going down the tree.
/// </summary>
/// <remarks>
///     <para>
///         <c>WriteAllocationTagMutation.Label</c> declares <c>[NotEmpty]</c> and sits at the <b>third</b>
///         level: order → line → allocation → tag. A rule holds, or does not, regardless of how deep the
///         branch carrying it is.
///     </para>
///     <para>
///         ⚠️ <b>The level matters because the chain can break.</b> A type's validator is generated only when
///         that type has rules <b>of its own</b>, and that validator is what walks its collections.
///         <c>WriteAllocationMutation</c> has no rules of its own, so it gets no validator, so it does not
///         walk <c>Tags</c>: a missing link opens between the tag and whoever could check it.
///     </para>
///     <para>
///         ⚠️ The control is the second test — the same send with the label filled — and the third measures
///         the <b>first</b> level, which is covered another way. If all three fell it would be the nested
///         write that is broken, not validation: they are different diagnoses.
///     </para>
/// </remarks>
public class TheRuleThreeLevelsDown(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    private static object[] LinesWith(string tagLabel, int quantity = 1) =>
    [
        new
        {
            id = Guid.NewGuid(),
            product = "Widget",
            quantity,
            allocations = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    warehouse = "milan",
                    quantity = 1,
                    slot = new { aisle = "A", shelf = 3 },
                    tags = new[] { new { id = Guid.NewGuid(), label = tagLabel, notes = Array.Empty<object>() } },
                },
            },
        },
    ];

    [Fact]
    public async Task ARuleOnTheThirdLevel_IsApplied()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/lines", LinesWith(tagLabel: ""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task TheControl_TheSameShapeWithAValidLabel_IsWritten()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/lines", LinesWith(tagLabel: "fragile"));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TheControl_TheFirstLevelIsCoveredToo()
    {
        var orderId = await AnOrderAsync();

        // quantity = 0 violates [GreaterThan(0)] on the first level, covered by the parent's validator.
        var response = await PutAsync($"/api/orders/{orderId}/lines", LinesWith("fragile", quantity: 0));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
