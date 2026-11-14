using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[LoadEntities&lt;T&gt;(nameof(Ids), Include = "Lines")]</c> on a real database (#730): every row comes with its navigation loaded.
/// </summary>
/// <remarks>
///     Through <c>CountLinesOfOrdersAction</c>, which answers how many lines each order has. Before #730 the list form had no <c>Include</c>,
///     and with no lazy loading every order's lines were empty: an action over several records read their navigations one by one or not at
///     all.
/// </remarks>
public class TheRowsNamedByAListWithTheirNavigations(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string Route = "/api/orders/line-counts";

    [Fact]
    public async Task EveryRow_ComesWithItsNavigation_InTheOrderOfTheKeys()
    {
        var two = await AnOrderAsync(2);
        var three = await AnOrderAsync(3);

        var counts = await ReadAsync(await PostAsync(Route, new[] { three, two }));

        counts.EnumerateArray().Select(c => c.GetInt32()).Should().Equal(3, 2);
    }

    /// <summary>The control: an order without lines counts 0 beside one that has them, so a count is the row's own.</summary>
    [Fact]
    public async Task AnOrderWithoutLines_CountsNone()
    {
        var none = await AnOrderAsync(0);
        var one = await AnOrderAsync(1);

        var counts = await ReadAsync(await PostAsync(Route, new[] { none, one }));

        counts.EnumerateArray().Select(c => c.GetInt32()).Should().Equal(0, 1);
    }

    private async Task<Guid> AnOrderAsync(int lines)
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Enumerable.Range(1, lines).Select(i => new { product = $"item-{i}", quantity = i }).ToArray(),
        }));

        return created.GetProperty("id").GetGuid();
    }
}
