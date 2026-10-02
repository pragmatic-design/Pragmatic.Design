using Conformance.Sales.Entities;
using Conformance.Sales.Mutations;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     An entity <b>handed over</b> to the invoker by a caller — the starting point.
/// </summary>
/// <remarks>
///     <para>
///         When the invoker reads by itself, it includes what it writes. When a domain action hands it a row
///         it already had in hand, nobody knows how much of the graph came with it — and skipping the load
///         «since we have the row anyway» is silent loss: the merge sees an empty collection, so it removes
///         nothing and <b>adds everything</b>.
///     </para>
///     <para>
///         ⚠️ These cases do not go through HTTP: the handover is a code API
///         (<c>InvokeAsync(mutation, entity, ct)</c>), and it is exactly the door no HTTP test crosses.
///     </para>
/// </remarks>
public class TheHandedOverEntity(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid OrderId, Guid LineId)> AnOrderWithOneLineAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[] { new { product = "bread", quantity = 1 } },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();
        return (created.GetProperty("id").GetGuid(), line.GetProperty("id").GetGuid());
    }

    /// <summary>
    ///     ⚠️ The central case: handing over an entity read <b>without</b> its children does not duplicate
    ///     them.
    /// </summary>
    /// <remarks>
    ///     Without the top-up this write would send back the same line and get two: the merge would not see
    ///     the existing one. No exception, no warning.
    /// </remarks>
    [Fact]
    public async Task AnEntityHandedOverWithoutItsChildren_DoesNotDuplicateThem()
    {
        var (orderId, lineId) = await AnOrderWithOneLineAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        db.ChangeTracker.Clear();

        // Read without Include: tracked, but with Lines empty and not loaded.
        var order = await db.Set<Order>().SingleAsync(o => o.PersistenceId == orderId);
        order.Lines.Should().BeEmpty("it was not included: it looks empty");

        var invoker = scope.ServiceProvider
            .GetRequiredService<global::Pragmatic.Actions.Invoker.IMutationInvoker<UpdateOrderLinesMutation, Order>>();

        var mutation = new UpdateOrderLinesMutation
        {
            Id = orderId,
            Lines =
            [
                new WriteOrderLineMutation { Id = lineId, Product = "bread", Quantity = 5 },
            ],
        };

        var result = await invoker.InvokeAsync(mutation, order, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{orderId}"));
        var lines = reread.GetProperty("lines").EnumerateArray().ToList();

        lines.Should().ContainSingle(
            "the existing line was recognized: without the top-up the merge would have added a second "
            + "one, because a collection never loaded is indistinguishable from an empty one");
        lines[0].GetProperty("id").GetGuid().Should().Be(lineId, "and it kept its identity");
        lines[0].GetProperty("quantity").GetInt32().Should().Be(5, "with the updated value");
    }

    /// <summary>
    ///     The cost: zero reads when the graph is already complete, one when something is missing.
    /// </summary>
    /// <remarks>
    ///     <c>INavigationLoader.EnsureLoadedAsync</c> returns the count on purpose: a promise about the
    ///     number of queries that nobody can check is no promise at all.
    /// </remarks>
    [Fact]
    public async Task TheTopUp_CostsNothingWhenTheGraphIsAlreadyComplete()
    {
        var (orderId, _) = await AnOrderWithOneLineAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        // The capability is discovered by cast, not registered separately: it is the contract the invoker
        // itself uses, so the case goes through the same door instead of inventing one of its own.
        var repository = (INavigationLoader<Order>)scope.ServiceProvider
            .GetRequiredService<IRepository<Order>>();

        db.ChangeTracker.Clear();
        var complete = await db.Set<Order>()
            .Include(o => o.Lines)
            .SingleAsync(o => o.PersistenceId == orderId);

        var reads = await repository.EnsureLoadedAsync(
            complete, ["Lines"], CancellationToken.None);

        reads.Should().Be(0, "it was already loaded: asking again would be a wasted round trip");

        db.ChangeTracker.Clear();
        var partial = await db.Set<Order>().SingleAsync(o => o.PersistenceId == orderId);

        var readsWhenMissing = await repository.EnsureLoadedAsync(
            partial, ["Lines"], CancellationToken.None);

        readsWhenMissing.Should().Be(1,
            "and when something is missing **one** is enough, with the includes needed — not one per row");
        partial.Lines.Should().ContainSingle("and after that read the graph is there");
    }
}
