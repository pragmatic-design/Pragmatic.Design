using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A mutation inherits what Mapping can do — the convergence.
/// </summary>
/// <remarks>
///     <para>
///         The case is <c>[MapProperty(Target = …)]</c> on a mutation: the name on the wire is
///         <c>orderCode</c>, the target is <c>Order.Reference</c>. Mapping owns the body of the write, so the
///         declared target is honoured; a second mapper would look for <c>Order.OrderCode</c>, not find it,
///         and report <c>PRAG0414</c> «missing setter <c>SetOrderCode()</c>» on a write Mapping performs
///         correctly.
///     </para>
///     <para>
///         ⚠️ <b>The case does not discriminate on its own.</b> A write that does not arrive and one that
///         arrives on the wrong field fail the same way: both leave <c>Reference</c> other than expected. The
///         control is the second test — <c>RenameOrderMutation</c> writes the same <c>Reference</c> by
///         <b>direct name</b>. If the declared-target path broke, that one would stay green and only the first
///         would turn red; if the write broke in general, both would fall. They are two different diagnoses.
///     </para>
/// </remarks>
public class TheDeclaredTarget(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync(string reference)
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference,
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ADeclaredTargetWritesTheEntityMemberItNames()
    {
        var orderId = await AnOrderAsync("ORD-BEFORE-1");

        var response = await PutAsync($"/api/orders/{orderId}/code", new
        {
            id = orderId,
            orderCode = "ORD-AFTER-01",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response);

        // The target is Reference, not a property called OrderCode: if the declared target were not read,
        // the previous value would remain.
        Assert.Equal("ORD-AFTER-01", body.GetProperty("reference").GetString());

        // ⚠️ And in the database too: the response is projected from the in-memory entity, so on its own it
        // cannot tell a write that happened from one the save did not carry away.
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var stored = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.PersistenceId == orderId);
        Assert.Equal("ORD-AFTER-01", stored.Reference);
    }

    [Fact]
    public async Task TheControl_TheSameFieldWrittenByDirectName()
    {
        var orderId = await AnOrderAsync("ORD-BEFORE-2");

        var response = await PutAsync($"/api/orders/{orderId}/reference", new
        {
            id = orderId,
            reference = "ORD-DIRECT-1",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response);

        Assert.Equal("ORD-DIRECT-1", body.GetProperty("reference").GetString());
    }
}
