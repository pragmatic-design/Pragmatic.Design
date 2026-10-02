using System.Linq;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A <c>[Patch&lt;T&gt;]</c> that carries children, applied over HTTP.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is the only write door for which <b>nobody loads</b>: <c>ApplyPatch</c> is invoked by
///         hand-written code, not by a generated invoker. And a patch merges — it decides what to remove by
///         looking at what is on the entity — so against a collection nobody loaded it removes nothing and
///         <b>adds everything</b>.
///     </para>
///     <para>
///         So the type publishes <c>WrittenNavigations</c>, and the caller passes it to
///         <c>EnsureLoadedAsync</c>. The two together are what this case measures end to end, which the
///         generator tests cannot.
///     </para>
/// </remarks>
public class ThePatchThatCarriesChildren(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid OrderId, Guid[] LineIds)> AnOrderWithTwoLinesAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new { product = "bread", quantity = 1 },
                new { product = "milk", quantity = 2 },
            },
        }));

        var lines = created.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid())
            .ToArray();

        return (created.GetProperty("id").GetGuid(), lines);
    }

    /// <summary>
    ///     ⚠️ Sending the same two lines back leaves two, not four.
    /// </summary>
    /// <remarks>
    ///     The merge saw what was there because the caller loaded what the patch declares it writes.
    ///     Without the list it would have included nothing, and every line sent would have looked new.
    /// </remarks>
    [Fact]
    public async Task SendingTheSameChildrenBack_DoesNotDuplicateThem()
    {
        // Deep and prefixed, like a mutation's: the composition is at runtime, from the list every child
        // publishes. ⚠️ The order's `Lines` navigation is generated from a relation, so the patch transform
        // must read the relation and not only the source — generator tests whose fixtures declare the
        // navigation by hand would not notice an empty list.
        Conformance.Sales.Entities.UpdateOrderPatch.WrittenNavigations
            .Should().BeEquivalentTo(
                new[] { "Lines", "Lines.Allocations", "Lines.Allocations.Tags", "Lines.Allocations.Tags.Notes" },
                "it is the list the caller passes to the loading");

        var (orderId, lineIds) = await AnOrderWithTwoLinesAsync();

        // ⚠️ The body IS the patch: with a single body property there is no object containing it.
        // Wrapping it in { "patch": … } would bind an empty patch, and the case would pass anyway because
        // «two before, two after» is indistinguishable from «nothing happened».
        var response = await PatchAsync($"/api/orders/{orderId}", new
        {
            reference = "ORD-PATCHED",
            lines = new[]
            {
                new { id = lineIds[0], product = "bread", quantity = 1 },
                new { id = lineIds[1], product = "milk", quantity = 2 },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);

        var updated = await ReadAsync(response);

        updated.GetProperty("lines").GetArrayLength().Should().Be(2,
            "the caller loaded what the patch declares it writes, so the merge recognized the lines "
            + "instead of adding them again");
    }

    /// <summary>
    ///     ⚠️ The control: the patch <b>really writes</b>, and does not remove what it does not name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is needed because the case above would pass even if <c>ApplyPatch</c> did not touch the
    ///         collection: «two after, two before» is true of a write that does not happen too — as with
    ///         a body wrapped in <c>{ "patch": … }</c>, which binds an empty patch.
    ///     </para>
    ///     <para>
    ///         And it measures the semantics that set a patch apart from a mutation: the strategy is
    ///         <c>AddOnly</c>, because a <c>[Patch&lt;T&gt;]</c> carries a <b>delta</b>. The line not named
    ///         stays; with a mutation, which carries the whole representation, it would disappear.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AndItStillWrites()
    {
        var (orderId, lineIds) = await AnOrderWithTwoLinesAsync();

        var response = await PatchAsync($"/api/orders/{orderId}", new
        {
            lines = new[] { new { id = lineIds[0], product = "bread", quantity = 7 } },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);

        var updated = await ReadAsync(response);
        var lines = updated.GetProperty("lines").EnumerateArray().ToList();

        lines.Should().HaveCount(2,
            "a patch is a delta: the line it does not name stays where it is — AddOnly, not Sync");

        var patched = lines.Single(l => l.GetProperty("id").GetGuid() == lineIds[0]);
        patched.GetProperty("quantity").GetInt32().Should().Be(7,
            "and the named one was really updated");
    }
}
