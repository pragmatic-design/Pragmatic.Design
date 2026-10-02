using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A collection the update does not mention.
/// </summary>
/// <remarks>
///     <para>
///         The single navigation has its own test (<c>AnUpdateThatDoesNotMentionTheChild_LeavesItAlone</c>);
///         the collection needs one too: a merge call emitted without a guard would let <c>Sync</c> read the
///         null as «the incoming set is empty» — that is, remove every row. A PUT that only renamed the order
///         would leave it without lines.
///     </para>
///     <para>
///         ⚠️ The control is the empty list: <c>[]</c> means «none», and it must keep removing them. Without
///         it, a guard that skipped every collection would pass the first case.
///     </para>
/// </remarks>
public class TheOmittedCollection(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderWithTwoLinesAsync()
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

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Not mentioning them does not remove them.</summary>
    [Fact]
    public async Task AnUpdateThatDoesNotMentionTheCollection_LeavesItAlone()
    {
        var orderId = await AnOrderWithTwoLinesAsync();

        var updated = await ReadAsync(await PutAsync($"/api/orders/{orderId}", new
        {
            reference = "ORD-RENAMED",
        }));

        updated.GetProperty("reference").GetString().Should().Be("ORD-RENAMED",
            "the write happened: without this assertion an update that does nothing would pass");
        updated.GetProperty("lines").GetArrayLength().Should().Be(2,
            "an absent collection is «I am not telling you about it», like the single navigation next to it");

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{orderId}"));
        reread.GetProperty("lines").GetArrayLength().Should().Be(2,
            "and so it is in the database, not only in the write's response");
    }

    /// <summary>And saying it — with an empty list — removes them: it is the strategy, not the guard.</summary>
    [Fact]
    public async Task AnEmptyCollection_RemovesEveryChild()
    {
        var orderId = await AnOrderWithTwoLinesAsync();

        var updated = await ReadAsync(await PutAsync($"/api/orders/{orderId}", new
        {
            reference = "ORD-EMPTIED",
            lines = Array.Empty<object>(),
        }));

        updated.GetProperty("lines").GetArrayLength().Should().Be(0,
            "[] is «no lines»: Sync removes what does not arrive, and here nothing arrives");
    }
}
