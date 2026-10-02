using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     1:N at depth 2 — <c>Order → Lines → Allocations</c> — through a mutation.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is the case that exercises the <b>prefixed</b> <c>WrittenNavigations</c>. The invoker must
///         include <c>Lines</c> and then <c>Lines.Allocations</c>: it derives the second from the list the
///         child DTO publishes, prefixing it with the navigation that reaches it.
///     </para>
///     <para>
///         If it stopped at the first level, the allocations would arrive unloaded and the merge would
///         rewrite them all: two rows sent back would become four, without exceptions. Measured here at two
///         levels.
///     </para>
///     <para>
///         Depth 2 through a mutation and an endpoint, not only on a DTO tree in
///         <c>Pragmatic.Mapping.EFCore.Tests</c>.
///     </para>
/// </remarks>
public class OneToManyDepth2(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid OrderId, Guid LineId, Guid AllocationId)> AnOrderWithAnAllocationAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new
                {
                    product = "bread",
                    quantity = 2,
                    allocations = new[]
                    {
                        new { warehouse = "milan", quantity = 1 },
                        new { warehouse = "rome", quantity = 1 },
                    },
                },
            },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();
        var allocation = line.GetProperty("allocations").EnumerateArray()
            .Single(a => a.GetProperty("warehouse").GetString() == "milan");

        return (created.GetProperty("id").GetGuid(),
                line.GetProperty("id").GetGuid(),
                allocation.GetProperty("id").GetGuid());
    }

    /// <summary>Creation builds the graph down to the second level.</summary>
    [Fact]
    public async Task Creating_ReachesTheSecondLevel()
    {
        var (orderId, _, _) = await AnOrderWithAnAllocationAsync();

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{orderId}"));

        var line = reread.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("allocations").GetArrayLength().Should().Be(2,
            "ToEntity builds the whole graph, and the re-read finds it in the database");
    }

    /// <summary>
    ///     ⚠️ The central case: updating keeps the identity <b>two levels down</b>.
    /// </summary>
    /// <remarks>
    ///     Had the invoker not included <c>Lines.Allocations</c>, the merge at the second level would find
    ///     nothing to update: the kept allocation would get a new id, and the one not sent would stay in the
    ///     database instead of disappearing.
    /// </remarks>
    [Fact]
    public async Task Updating_KeepsTheIdentityTwoLevelsDown()
    {
        var (orderId, lineId, keptAllocationId) = await AnOrderWithAnAllocationAsync();

        var updated = await ReadAsync(await PutAsync($"/api/orders/{orderId}/lines", new[]
        {
            new
            {
                id = lineId,
                product = "bread",
                quantity = 2,
                allocations = new[]
                {
                    new { id = keptAllocationId, warehouse = "milan", quantity = 7 }, // exists
                    new { id = Guid.Empty, warehouse = "naples", quantity = 3 },      // new
                },
            },
        }));

        var line = updated.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("id").GetGuid().Should().Be(lineId, "the first level stays merged");

        var allocations = line.GetProperty("allocations").EnumerateArray().ToList();
        allocations.Should().HaveCount(2, "one updated, one created, one removed");

        var kept = allocations.Single(a => a.GetProperty("warehouse").GetString() == "milan");
        kept.GetProperty("id").GetGuid().Should().Be(keptAllocationId,
            "it keeps its id two levels down: the invoker included Lines.Allocations by prefixing "
            + "the list OrderLineDto publishes. Without that include the id would have changed");
        kept.GetProperty("quantity").GetInt32().Should().Be(7,
            "and the value two levels down arrived");

        allocations.Should().Contain(a => a.GetProperty("warehouse").GetString() == "naples");
        allocations.Should().NotContain(a => a.GetProperty("warehouse").GetString() == "rome",
            "and what does not arrive goes away, at the second level too");
    }

    /// <summary>The second level is really in the database, not only in the response.</summary>
    [Fact]
    public async Task TheSecondLevel_SurvivesASeparateRead()
    {
        var (orderId, lineId, keptAllocationId) = await AnOrderWithAnAllocationAsync();

        await PutAsync($"/api/orders/{orderId}/lines", new[]
        {
            new
            {
                id = lineId,
                product = "bread",
                quantity = 2,
                allocations = new[]
                {
                    new { id = keptAllocationId, warehouse = "milan", quantity = 42 },
                },
            },
        });

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{orderId}"));

        var allocations = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().ToList();

        allocations.Should().ContainSingle("the removal at the second level was persisted");
        allocations[0].GetProperty("quantity").GetInt32().Should().Be(42);
    }
}
