using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[Sort(Nulls = NullOrder.Last)]</c> on EF Core, PostgreSQL, over HTTP (#733): the orders without notes come last, ascending and
///     descending.
/// </summary>
/// <remarks>
///     Through <c>ListOrdersByNotesQuery</c>. PostgreSQL orders nulls last ascending and first descending, so the descending case is the one
///     the declaration changes; the ascending one is its control.
/// </remarks>
public class TheNullKeysGoWhereTheSortSays(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Descending_TheOrdersWithoutNotes_ComeLast()
    {
        var prefix = await OrdersAsync();

        (await ReferencesAsync($"/api/orders/by-notes?reference={prefix}&notesSort=Descending"))
            .Should().Equal(prefix + "-b", prefix + "-a", prefix + "-none");
    }

    [Fact]
    public async Task Ascending_TheOrdersWithoutNotes_ComeLast()
    {
        var prefix = await OrdersAsync();

        (await ReferencesAsync($"/api/orders/by-notes?reference={prefix}"))
            .Should().Equal(prefix + "-a", prefix + "-b", prefix + "-none");
    }

    private async Task<List<string?>> ReferencesAsync(string route)
    {
        // An unpaged query (no Page/PageSize): the body is the list itself.
        var rows = await ReadAsync(await Client.GetAsync(route));
        return [.. rows.EnumerateArray().Select(o => o.GetProperty("reference").GetString())];
    }

    /// <summary>Three orders of one prefix: notes "a", notes "b", no notes; written through the context, as TheRowsNamedByAList writes its labels.</summary>
    private async Task<string> OrdersAsync()
    {
        var prefix = $"NUL-{Guid.NewGuid():N}"[..10];
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        foreach (var (suffix, notes) in new[] { ("none", (string?)null), ("b", "b"), ("a", "a") })
        {
            var order = Order.Create();
            db.Add(order);
            db.Entry(order).Property(nameof(Order.Reference)).CurrentValue = prefix + "-" + suffix;
            db.Entry(order).Property(nameof(Order.Notes)).CurrentValue = notes;
        }

        await db.SaveChangesAsync();
        return prefix;
    }
}
