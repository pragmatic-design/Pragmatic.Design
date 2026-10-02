using System.Net;
using Conformance.Catalog.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[UndoWith&lt;T&gt;]</c>: the step that already committed across the boundary is undone when
///     its caller fails.
/// </summary>
/// <remarks>
///     <para>
///         The boundary is the transaction boundary: <c>StockOrderThroughMutation</c> calls the catalog,
///         which saves first and on its own, and then fails. Without a declared decision the item would
///         stay written — which is what <c>PRAG0424</c> warns about, and what leaves orphan rows in a real
///         application.
///     </para>
///     <para>
///         ⚠️ <b>The pair is the case.</b> «The item is not there» alone is also satisfied by a step that
///         never wrote it, so the control is the very same call with <c>fail = false</c>: there the item
///         is there. What changes between the two is only the caller's outcome, not what the catalog did.
///     </para>
///     <para>
///         ⚠️ What this case does <b>not</b> measure, because the attribute does not promise it:
///         durability. A process that dies between the inner commit and the compensation leaves the item
///         written, and that is where a saga begins.
///     </para>
///     <para>
///         Measured by removal, the two halves separately. Without the attribute the <b>build</b> fails:
///         <c>PRAG0424</c> is an error under <c>--warnaserror</c>, so the declaration is load-bearing at
///         compile time and no version of the example exists without it. With the compensator's body
///         emptied instead — which compiles — <b>only</b> the first of the two tests goes red. The two
///         measures together say that the attribute answers the diagnostic <em>and</em> that the undo
///         really runs.
///     </para>
/// </remarks>
public class TheStepThatUndoesItself(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task WhenTheCallerFails_TheCommittedItemIsRemoved()
    {
        var orderId = await AnOrderAsync();
        var itemName = $"undone-{Guid.NewGuid():N}"[..20];

        var response = await PutAsync($"/api/orders/{orderId}/stocked-classification", new
        {
            itemName,
            fail = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the outer step is what fails, after the catalog committed — " +
            await response.Content.ReadAsStringAsync());

        (await ItemExistsAsync(itemName)).Should().BeFalse(
            "the undo declared with [UndoWith<RemoveAddedCatalogItem>] runs before the response goes back");
    }

    /// <summary>The control: the same call succeeding leaves the item where it is.</summary>
    [Fact]
    public async Task WhenTheCallerSucceeds_TheItemStays()
    {
        var orderId = await AnOrderAsync();
        var itemName = $"kept-{Guid.NewGuid():N}"[..20];

        var response = await PutAsync($"/api/orders/{orderId}/stocked-classification", new
        {
            itemName,
            fail = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        (await ItemExistsAsync(itemName)).Should().BeTrue(
            "without this, «the item is not there» would be satisfied by a step that writes nothing");
    }

    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    private async Task<bool> ItemExistsAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return await catalog.Set<CatalogItem>().AsNoTracking().AnyAsync(i => i.Name == name);
    }
}
