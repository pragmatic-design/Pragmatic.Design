using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     1:N at depth 3 — <c>Order → Lines → Allocations → Tags</c> — and a value object inside a written
///     child.
/// </summary>
/// <remarks>
///     <para>
///         Two cells in a single case, because they share the same write.
///     </para>
///     <para>
///         <b>Depth 3</b>, executed: a snapshot that names four levels compares the generated
///         <em>text</em> and does not send it to a database.
///     </para>
///     <para>
///         <b>A value object inside a written child</b>: counting the elements without looking at their
///         content would not prove it, and neither would a value object written only at the root. Here
///         <c>Allocation.Slot</c> is a <c>[ValueObject]</c> two levels below the root, and the question is
///         whether its columns survive the merge.
///     </para>
///     <para>
///         The mechanism under test is the <b>two-hop</b> prefix: the invoker must include
///         <c>Lines.Allocations.Tags</c>, obtained by putting <c>Lines</c> in front of
///         <c>Allocations.Tags</c> that <c>OrderLineDto</c> publishes — itself obtained by prefixing
///         <c>AllocationDto</c>'s <c>Tags</c>.
///     </para>
///     <para>
///         ⚠️ <b>The first two cases do not isolate it, and do not pretend to.</b> The invoker composes the
///         includes from two sources — the children's write list and the response DTO's read list — and
///         answering with <c>OrderDto</c>, which reaches <c>Lines.Allocations.Tags</c>, the two overlap: a
///         defect in the first would stay covered by the second. Those cases are green on the
///         <em>result</em> — the three-level graph is written and persists — not on the path that loads
///         it. <see cref="TheWriteListAlone_LoadsTheThirdLevel" />, which answers with a flat shape,
///         isolates the first source.
///     </para>
/// </remarks>
public class OneToManyDepth3(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private sealed record Seeded(Guid OrderId, Guid LineId, Guid AllocationId, Guid TagId);

    private async Task<Seeded> AnOrderThreeLevelsDeepAsync()
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
                        new
                        {
                            warehouse = "milan",
                            quantity = 1,
                            slot = new { aisle = "A", shelf = 3 },
                            tags = new[]
                            {
                                new { label = "fragile" },
                                new { label = "urgent" },
                            },
                        },
                    },
                },
            },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();
        var allocation = line.GetProperty("allocations").EnumerateArray().Single();
        var tag = allocation.GetProperty("tags").EnumerateArray()
            .Single(t => t.GetProperty("label").GetString() == "fragile");

        return new Seeded(
            created.GetProperty("id").GetGuid(),
            line.GetProperty("id").GetGuid(),
            allocation.GetProperty("id").GetGuid(),
            tag.GetProperty("id").GetGuid());
    }

    /// <summary>Creation builds the graph down to the third level, value object included.</summary>
    [Fact]
    public async Task Creating_ReachesTheThirdLevel()
    {
        var seed = await AnOrderThreeLevelsDeepAsync();

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{seed.OrderId}"));

        var allocation = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single();

        allocation.GetProperty("tags").GetArrayLength().Should().Be(2,
            "ToEntity builds the whole graph, and the re-read finds it in the database");

        var slot = allocation.GetProperty("slot");
        slot.GetProperty("aisle").GetString().Should().Be("A",
            "the value object two levels down reached its columns");
        slot.GetProperty("shelf").GetInt32().Should().Be(3);
    }

    /// <summary>
    ///     ⚠️ The central case: updating keeps the identity <b>three levels down</b>, and rewrites the
    ///     value object next to it.
    /// </summary>
    [Fact]
    public async Task Updating_KeepsTheIdentityThreeLevelsDown()
    {
        var seed = await AnOrderThreeLevelsDeepAsync();

        var updated = await ReadAsync(await PutAsync($"/api/orders/{seed.OrderId}/lines", new[]
        {
            new
            {
                id = seed.LineId,
                product = "bread",
                quantity = 2,
                allocations = new[]
                {
                    new
                    {
                        id = seed.AllocationId,
                        warehouse = "milan",
                        quantity = 1,
                        slot = new { aisle = "B", shelf = 9 },   // the value object changes
                        tags = new[]
                        {
                            new { id = seed.TagId, label = "fragile" },  // exists
                            new { id = Guid.Empty, label = "express" },  // new
                        },                                              // "urgent" does not arrive
                    },
                },
            },
        }));

        var allocation = updated.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single();

        allocation.GetProperty("id").GetGuid().Should().Be(seed.AllocationId,
            "the second level stays merged");

        var tags = allocation.GetProperty("tags").EnumerateArray().ToList();
        tags.Should().HaveCount(2, "one updated, one created, one removed");

        var kept = tags.Single(t => t.GetProperty("label").GetString() == "fragile");
        kept.GetProperty("id").GetGuid().Should().Be(seed.TagId,
            "it keeps its id three levels down: the invoker included Lines.Allocations.Tags, "
            + "prefixing twice. Without that include the id would have changed");

        tags.Should().Contain(t => t.GetProperty("label").GetString() == "express");
        tags.Should().NotContain(t => t.GetProperty("label").GetString() == "urgent",
            "and what does not arrive goes away, at the third level too");
    }

    /// <summary>
    ///     ⚠️ The case that <b>isolates the variable</b>: the write list alone loads the third level.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same write as the cases above, but through a route that answers with
    ///         <c>OrderSummaryDto</c>, whose <c>RequiredNavigations</c> is <b>empty</b>. The only thing that
    ///         can load the grandchildren is <c>OrderLineDto.WrittenNavigations</c> prefixed twice: if that
    ///         prefix stopped at one hop, the merge at the third level would find the tags unloaded and
    ///         rewrite them all, with new ids.
    ///     </para>
    ///     <para>
    ///         The check is deliberately on a <b>second read</b>, not on the response: the response here
    ///         does not contain the tags, and that is exactly the point.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheWriteListAlone_LoadsTheThirdLevel()
    {
        var seed = await AnOrderThreeLevelsDeepAsync();

        var answer = await ReadAsync(await PutAsync($"/api/orders/{seed.OrderId}/lines/quietly", new[]
        {
            new
            {
                id = seed.LineId,
                product = "bread",
                quantity = 2,
                allocations = new[]
                {
                    new
                    {
                        id = seed.AllocationId,
                        warehouse = "milan",
                        quantity = 1,
                        slot = new { aisle = "A", shelf = 3 },
                        tags = new[] { new { id = seed.TagId, label = "fragile" } },
                    },
                },
            },
        }));

        answer.TryGetProperty("lines", out _).Should().BeFalse(
            "the response is the flat shape: that is what takes the second include source out of the way");

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{seed.OrderId}"));

        var tags = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single()
            .GetProperty("tags").EnumerateArray().ToList();

        tags.Should().ContainSingle("«urgent» was not sent back, so it went away");
        tags[0].GetProperty("id").GetGuid().Should().Be(seed.TagId,
            "and «fragile» kept its id: the write list alone loaded it, "
            + "prefixed from AllocationDto to OrderLineDto and then by the invoker");
    }

    /// <summary>
    ///     The value object inside a written child: not the element, the <b>content</b>.
    /// </summary>
    [Fact]
    public async Task AValueObjectInsideAWrittenChild_IsRewritten()
    {
        var seed = await AnOrderThreeLevelsDeepAsync();

        await PutAsync($"/api/orders/{seed.OrderId}/lines", new[]
        {
            new
            {
                id = seed.LineId,
                product = "bread",
                quantity = 2,
                allocations = new[]
                {
                    new
                    {
                        id = seed.AllocationId,
                        warehouse = "milan",
                        quantity = 1,
                        slot = new { aisle = "B", shelf = 9 },
                        tags = new[] { new { id = seed.TagId, label = "fragile" } },
                    },
                },
            },
        });

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{seed.OrderId}"));

        var slot = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single()
            .GetProperty("slot");

        slot.GetProperty("aisle").GetString().Should().Be("B",
            "both columns of the complex type were rewritten by the merge, "
            + "on a row reached two levels below the root");
        slot.GetProperty("shelf").GetInt32().Should().Be(9);
    }
}
