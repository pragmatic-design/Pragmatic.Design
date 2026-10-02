using Pragmatic.Messaging;
using Pragmatic.Messaging.Testing;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     <see cref="MessageBusTestHarness" />: an <see cref="IMessageBus" /> test
///     double for unit tests. It records every <c>SendAsync</c>,
///     <c>PublishAsync</c>, and <c>DispatchAsync</c> into the inspectable
///     <see cref="MessageBusTestHarness.Published" /> bag (and intentionally does
///     NOT dispatch to handlers), so a unit under test can be driven and asserted
///     without a real bus or transport. Assertions use
///     <see cref="MessageBusTestHarness.PublishedOf{T}" /> and
///     <see cref="MessageBusTestHarness.HasPublished{T}(System.Func{T, bool})" />.
/// </summary>
public static class TestingHarnessSample
{
    public sealed record OrderSubmitted(Guid OrderId, decimal Total);
    public sealed record AuditLogged(string Action);

    /// <summary>The unit under test — depends only on IMessageBus.</summary>
    public sealed class Checkout(IMessageBus bus)
    {
        public async Task SubmitAsync(Guid orderId, decimal total)
        {
            await bus.PublishAsync(new OrderSubmitted(orderId, total));
            await bus.PublishAsync(new AuditLogged("order-submitted"));
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Testing harness (MessageBusTestHarness) ---");

        var harness = new MessageBusTestHarness();
        var checkout = new Checkout(harness);

        await checkout.SubmitAsync(Guid.NewGuid(), 250.00m);
        await checkout.SubmitAsync(Guid.NewGuid(), 80.00m);

        Console.WriteLine($"  total recorded messages  : {harness.Published.Count} (expected 4)");
        Console.WriteLine($"  OrderSubmitted recorded   : {harness.PublishedOf<OrderSubmitted>().Count} (expected 2)");
        Console.WriteLine($"  AuditLogged recorded      : {harness.PublishedOf<AuditLogged>().Count} (expected 2)");

        var bigOrder = harness.HasPublished<OrderSubmitted>(o => o.Total >= 200m);
        Console.WriteLine($"  any order >= 200          : {bigOrder} (predicate assertion)");

        harness.Reset();
        Console.WriteLine($"  after Reset()             : {harness.Published.Count} recorded");
        Console.WriteLine();
    }
}
