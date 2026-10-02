using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A <c>[Patch&lt;T&gt;]</c> over HTTP tells a null that was sent from a property that was not sent.
/// </summary>
/// <remarks>
///     <para>
///         <c>ApplyPatch</c> has two branches: the one that honours <c>_setProperties</c> and the fallback
///         «write what is not null». Without a caller of <c>MarkSet</c> the wire would always take the second,
///         and a <c>{"notes": null}</c> would arrive like a body without <c>notes</c>, clearing nothing. The
///         JSON converter generated on the type calls it.
///     </para>
///     <para>
///         The check looks at the database, not the response: <c>OrderDto</c> does not expose <c>Notes</c>,
///         and that is where the defect would be.
///     </para>
/// </remarks>
public class TheExplicitNull(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderWithNotesAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var orderId = created.GetProperty("id").GetGuid();

        var response = await PatchAsync($"/api/orders/{orderId}", new { notes = "fragile" });
        response.EnsureSuccessStatusCode();

        (await NotesOfAsync(orderId)).Should().Be("fragile", "the value arrived: it is the base of the two cases");

        return orderId;
    }

    private async Task<string?> NotesOfAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await db.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == orderId);
        return order.Notes;
    }

    /// <summary>⚠️ A null that was sent clears.</summary>
    [Fact]
    public async Task ANullThatWasSent_ClearsTheValue()
    {
        var orderId = await AnOrderWithNotesAsync();

        var response = await PatchAsync($"/api/orders/{orderId}", new { notes = (string?)null });
        response.EnsureSuccessStatusCode();

        (await NotesOfAsync(orderId)).Should().BeNull(
            "the body named notes with a null: the converter marks it and ApplyPatch applies it");
    }

    /// <summary>The control: a property that was not sent stays as it is.</summary>
    /// <remarks>
    ///     Without it, a converter that marked every property — sent or not — would pass the case above by
    ///     clearing everything.
    /// </remarks>
    [Fact]
    public async Task APropertyThatWasNotSent_IsLeftAlone()
    {
        var orderId = await AnOrderWithNotesAsync();

        var response = await PatchAsync($"/api/orders/{orderId}", new { reference = "ORD-PATCHED" });
        response.EnsureSuccessStatusCode();

        (await NotesOfAsync(orderId)).Should().Be("fragile", "the body did not name notes");
    }
}
