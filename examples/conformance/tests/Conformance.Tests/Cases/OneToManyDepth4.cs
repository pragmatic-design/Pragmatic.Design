using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     1:N at depth 4 — <c>Order → Lines → Allocations → Tags → Notes</c>.
/// </summary>
/// <remarks>
///     <para>
///         Depth four executed on a database: a snapshot that names four levels compares the generated
///         <em>text</em> and does not send it to a database.
///     </para>
///     <para>
///         ⚠️ The mechanism is the <b>three-hop</b> prefix. The path <c>Lines.Allocations.Tags.Notes</c> is
///         composed going up: <c>AllocationTagDto</c> publishes <c>Notes</c>, <c>AllocationDto</c> prefixes
///         it into <c>Tags.Notes</c>, <c>OrderLineDto</c> into <c>Allocations.Tags.Notes</c>, and the invoker
///         puts <c>Lines</c> in front.
///     </para>
///     <para>
///         The case goes through the route that <b>isolates</b> — the one answering with
///         <c>OrderSummaryDto</c>, whose read list is empty — because otherwise it would be green with a
///         broken prefix too: the lesson of <see cref="OneToManyDepth3" />.
///     </para>
/// </remarks>
public class OneToManyDepth4(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private sealed record Seeded(Guid OrderId, Guid LineId, Guid AllocationId, Guid TagId, Guid NoteId);

    private async Task<Seeded> AnOrderFourLevelsDeepAsync()
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
                                new
                                {
                                    label = "fragile",
                                    notes = new[]
                                    {
                                        new { text = "handle with care" },
                                        new { text = "do not stack" },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();
        var allocation = line.GetProperty("allocations").EnumerateArray().Single();
        var tag = allocation.GetProperty("tags").EnumerateArray().Single();
        var note = tag.GetProperty("notes").EnumerateArray()
            .Single(n => n.GetProperty("text").GetString() == "handle with care");

        return new Seeded(
            created.GetProperty("id").GetGuid(),
            line.GetProperty("id").GetGuid(),
            allocation.GetProperty("id").GetGuid(),
            tag.GetProperty("id").GetGuid(),
            note.GetProperty("id").GetGuid());
    }

    /// <summary>Creation builds the graph down to the fourth level.</summary>
    [Fact]
    public async Task Creating_ReachesTheFourthLevel()
    {
        var seed = await AnOrderFourLevelsDeepAsync();

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{seed.OrderId}"));

        var notes = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single()
            .GetProperty("tags").EnumerateArray().Single()
            .GetProperty("notes").EnumerateArray().ToList();

        notes.Should().HaveCount(2,
            "ToEntity builds the whole graph, and the re-read finds it in the database");
    }

    /// <summary>
    ///     ⚠️ Updating keeps the identity <b>four levels down</b>, with the write list alone loading the path.
    /// </summary>
    [Fact]
    public async Task TheWriteListAlone_KeepsTheIdentityFourLevelsDown()
    {
        var seed = await AnOrderFourLevelsDeepAsync();

        await PutAsync($"/api/orders/{seed.OrderId}/lines/quietly", new[]
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
                        tags = new[]
                        {
                            new
                            {
                                id = seed.TagId,
                                label = "fragile",
                                notes = new[]
                                {
                                    new { id = seed.NoteId, text = "handle, really" },
                                },                       // «do not stack» does not arrive
                            },
                        },
                    },
                },
            },
        });

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{seed.OrderId}"));

        var notes = reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single()
            .GetProperty("tags").EnumerateArray().Single()
            .GetProperty("notes").EnumerateArray().ToList();

        notes.Should().ContainSingle("what does not arrive goes away, at the fourth level too");
        notes[0].GetProperty("id").GetGuid().Should().Be(seed.NoteId,
            "it keeps its id four levels down: the write list alone loaded the path, "
            + "prefixed three times up to Lines.Allocations.Tags.Notes");
        notes[0].GetProperty("text").GetString().Should().Be("handle, really",
            "and the value four levels down arrived");
    }
}
