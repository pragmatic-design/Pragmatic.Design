using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Generated;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Samples.Samples;

/// <summary>
///     End-to-end saga demo — drives <see cref="OrderSaga"/> through three scenarios
///     so an external dev can see start/compensation/timeout happening in a
///     single console run without any infrastructure (no Postgres, no RabbitMQ):
///
///     1. Happy path         — OrderRequested → PaymentReceived → OrderShipped → Completed
///     2. Compensation chain — ShipOrderAction handler throws, the SG-generated catch
///                              block dispatches RefundPaymentAction through the bus
///     3. Timeout            — [SagaTimeout(Duration="00:00:02")] on the PaymentReceived
///                              step; we force the deadline into the past and invoke
///                              the ISagaTimeoutRunner directly (BG service poll would
///                              work too, the runner is just easier to await in a sample)
/// </summary>
public static class SagaFlowSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Saga flow (start / compensation / timeout) ---");

        await RunHappyPathAsync();
        await RunCompensationOnExceptionAsync();
        await RunTimeoutAsync();

        Console.WriteLine();
    }

    // =========================================================================
    // 1. Happy path
    // =========================================================================

    private static async Task RunHappyPathAsync()
    {
        var ship = new ShipOrderObserver();
        var refund = new RefundObserver();

        await using var sp = BuildProvider(ship, refund, shipThrows: false);
        var bus = sp.GetRequiredService<IMessageBus>();
        var repo = sp.GetRequiredService<ISagaRepository<OrderSaga>>();

        var orderId = Guid.NewGuid();
        var correlationId = orderId.ToString();

        await DispatchAsync(sp, new OrderRequested(orderId, 149.95m, "C-1"));
        await DispatchAsync(sp, new PaymentReceived(orderId, "TX-1"));
        await DispatchAsync(sp, new OrderShipped(orderId, "TRK-1"));

        var saga = await repo.FindByCorrelationAsync(correlationId);
        Console.WriteLine($"  [1/3] happy path        : state={saga!.State}, ship-called={ship.Count}, refund-called={refund.Count}");
    }

    // =========================================================================
    // 2. Compensation on exception
    // =========================================================================

    private static async Task RunCompensationOnExceptionAsync()
    {
        var ship = new ShipOrderObserver();
        var refund = new RefundObserver();

        // The saga step throws inside its own handler via OrderSaga.ThrowOnNextPaymentReceived.
        // We model the throw inside user code rather than from the ShipOrderAction probe
        // handler because the in-memory bus intentionally isolates handler failures (fan-out
        // safety). Real sagas typically throw out of the service calls the step makes — same
        // net effect: the SG-generated try/catch runs DispatchCompensation_Handle, which
        // publishes RefundPaymentAction through the bus, and the orchestrator re-throws.
        await using var sp = BuildProvider(ship, refund, shipThrows: false);
        var repo = sp.GetRequiredService<ISagaRepository<OrderSaga>>();

        var orderId = Guid.NewGuid();
        var correlationId = orderId.ToString();

        await DispatchAsync(sp, new OrderRequested(orderId, 75.00m, "C-2"));

        OrderSaga.ThrowOnNextPaymentReceived = true;
        try
        {
            await DispatchAsync(sp, new PaymentReceived(orderId, "TX-2"));
        }
        catch (InvalidOperationException)
        {
            // Expected: the orchestrator re-throws after running the compensation chain.
        }

        var saga = await repo.FindByCorrelationAsync(correlationId);
        Console.WriteLine($"  [2/3] compensation      : state={saga!.State}, ship-called={ship.Count}, refund-called={refund.Count} (expected 1)");
    }

    // =========================================================================
    // 3. Timeout
    // =========================================================================

    private static async Task RunTimeoutAsync()
    {
        var ship = new ShipOrderObserver();
        var refund = new RefundObserver();

        await using var sp = BuildProvider(ship, refund, shipThrows: false);
        var repo = sp.GetRequiredService<ISagaRepository<OrderSaga>>();

        var orderId = Guid.NewGuid();
        var correlationId = orderId.ToString();

        await DispatchAsync(sp, new OrderRequested(orderId, 22.50m, "C-3"));
        await DispatchAsync(sp, new PaymentReceived(orderId, "TX-3"));
        // Do NOT dispatch OrderShipped. The PaymentReceived step set TimeoutAt = UtcNow + 2s.

        var saga = await repo.FindByCorrelationAsync(correlationId);
        await repo.SetTimeoutAsync(saga!.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        // Pull the SG-generated ISagaTimeoutRunner directly rather than waiting on
        // the hosted background service — keeps the sample deterministic.
        using var scope = sp.CreateScope();
        var runners = scope.ServiceProvider.GetServices<ISagaTimeoutRunner>();
        foreach (var runner in runners)
            await runner.RunDueTimeoutsAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var inMem = (InMemorySagaRepository<OrderSaga>)repo;
        Console.WriteLine($"  [3/3] timeout           : state={saga.State}, timed-out={inMem.IsTimedOut(saga.Id)}, refund-called={refund.Count} (expected 1)");
    }

    // =========================================================================
    // Plumbing
    // =========================================================================

    private static ServiceProvider BuildProvider(ShipOrderObserver ship, RefundObserver refund, bool shipThrows)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddPragmaticSagas();

        // Probe handlers for the actions the saga publishes. Only ShipOrderAction needs
        // to behave differently across scenarios, so we inject the flag via a delegate.
        services.AddSingleton(ship);
        services.AddSingleton(refund);
        services.AddSingleton<IMessageHandler<ShipOrderAction>>(
            new ShipHandler(ship, shipThrows));
        services.AddSingleton<IMessageHandler<RefundPaymentAction>>(
            new RefundHandler(refund));

        return services.BuildServiceProvider();
    }

    private static async Task DispatchAsync<T>(IServiceProvider sp, T message) where T : notnull
    {
        // Dispatching through the SG-generated IMessageHandler<T> (the saga's
        // EventHandler_<Event>) mirrors what a transport consumer does after it
        // pulls a message off the wire.
        using var scope = sp.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IMessageHandler<T>>())
            await handler.HandleAsync(message, MessageContext.New());
    }

    // =========================================================================
    // Probe handlers
    // =========================================================================

    public sealed class ShipOrderObserver { public int Count; }
    public sealed class RefundObserver { public int Count; }

    public sealed class ShipHandler(ShipOrderObserver observer, bool throws) : IMessageHandler<ShipOrderAction>
    {
        public Task HandleAsync(ShipOrderAction message, MessageContext context, CancellationToken ct)
        {
            observer.Count++;
            if (throws)
                throw new InvalidOperationException("shipping provider unreachable (simulated)");
            return Task.CompletedTask;
        }
    }

    public sealed class RefundHandler(RefundObserver observer) : IMessageHandler<RefundPaymentAction>
    {
        public Task HandleAsync(RefundPaymentAction message, MessageContext context, CancellationToken ct)
        {
            observer.Count++;
            return Task.CompletedTask;
        }
    }
}
