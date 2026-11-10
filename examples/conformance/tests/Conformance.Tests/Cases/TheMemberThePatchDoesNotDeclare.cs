using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A <c>[Patch&lt;T&gt;]</c> body over HTTP that names a member the patch does not declare is read, and the member ignored (#631).
/// </summary>
/// <remarks>
///     A front end that sends back the record it read sends its <c>id</c> too, and <c>UpdateOrderPatch</c> declares none: the request is
///     about the order the route names, and the patch is what changes. Answered 400 "could not be converted" by an application on the
///     packages.
/// </remarks>
public class TheMemberThePatchDoesNotDeclare(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task AMemberThePatchDoesNotDeclare_IsIgnored_AndTheRestIsApplied()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));
        var orderId = created.GetProperty("id").GetGuid();

        var response = await PatchAsync($"/api/orders/{orderId}", new
        {
            id = orderId,
            notes = "after the id",
            somethingElse = new { nested = new[] { 1, 2 } },
        });
        await ReadAsync(response);

        await using var scope = Services.CreateAsyncScope();
        var order = await scope.ServiceProvider.GetRequiredService<SalesDbContext>()
            .Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == orderId);
        order.Notes.Should().Be("after the id", "the members after the unknown one are still read and applied");
    }
}
