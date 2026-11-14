using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Events.Tests;

/// <summary>
///     A handler dispatched inline runs in an <see cref="OperationScope" /> of its own, and the raising operation's is current again after
///     it (#738).
/// </summary>
/// <remarks>
///     <para>
///         The invokers dispatch after the commit, still inside the raising operation's scope. The handler read the raiser's name as its
///         own, so what it wrote without an invoker of its own (a repository and a save) was attributed by the audit trail to the
///         operation that raised the event.
///     </para>
///     <para>
///         A handler that invokes an operation was attributed correctly already: that invoker enters its own scope and restores the one it
///         found. The control is that the raiser is current again afterwards, whatever the handler did, a throw included.
///     </para>
/// </remarks>
public class AnInlineHandlerRunsInAnOperationOfItsOwnTests
{
    private const string Raiser = "App.Orders.PlaceOrderMutation";

    private sealed record OrderPlaced(string Number) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task TheHandler_ReadsItsOwnName_AndTheRaiserIsCurrentAfterwards()
    {
        var handler = new ObservingHandler();
        var dispatcher = Dispatcher(services => services.AddSingleton<IDomainEventHandler<OrderPlaced>>(handler));

        using (OperationScope.Enter(Raiser))
        {
            await dispatcher.DispatchAsync(new OrderPlaced("A-1"));

            handler.Seen.Should().Be(typeof(ObservingHandler).FullName, "the handler is an operation of its own, not the raiser");
            OperationScope.Current.Should().Be(Raiser, "the raiser's scope is restored once the handler returns");
        }
    }

    [Fact]
    public async Task AnOperationTheHandlerInvokes_IsNestedInTheHandlers()
    {
        var handler = new NestingHandler();
        var dispatcher = Dispatcher(services => services.AddSingleton<IDomainEventHandler<OrderPlaced>>(handler));

        using (OperationScope.Enter(Raiser))
            await dispatcher.DispatchAsync(new OrderPlaced("A-2"));

        handler.Inside.Should().Be("App.Shipping.ReserveStockMutation");
        handler.AfterTheInner.Should().Be(typeof(NestingHandler).FullName, "the inner operation restores the handler's, not the raiser's");
    }

    [Fact]
    public async Task AHandlerThatThrows_LeavesTheRaiserCurrent()
    {
        var dispatcher = Dispatcher(services => services.AddSingleton<IDomainEventHandler<OrderPlaced>>(new ThrowingHandler()));

        using (OperationScope.Enter(Raiser))
        {
            await dispatcher.DispatchAsync(new OrderPlaced("A-3"));

            OperationScope.Current.Should().Be(Raiser);
        }
    }

    [Fact]
    public async Task OutsideAnyOperation_TheHandlerStillHasItsName()
    {
        var handler = new ObservingHandler();
        var dispatcher = Dispatcher(services => services.AddSingleton<IDomainEventHandler<OrderPlaced>>(handler));

        await dispatcher.DispatchAsync(new OrderPlaced("A-4"));

        handler.Seen.Should().Be(typeof(ObservingHandler).FullName, "an outbox delivery runs outside any operation, and the handler is still one");
        OperationScope.Current.Should().BeNull();
    }

    private static InMemoryEventDispatcher Dispatcher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return new InMemoryEventDispatcher(services.BuildServiceProvider(), NullLogger<InMemoryEventDispatcher>.Instance,
            [new PassthroughEventDispatchTable()]);
    }

    private sealed class ObservingHandler : IDomainEventHandler<OrderPlaced>
    {
        public string? Seen { get; private set; }

        public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
        {
            Seen = OperationScope.Current;
            return Task.CompletedTask;
        }
    }

    private sealed class NestingHandler : IDomainEventHandler<OrderPlaced>
    {
        public string? Inside { get; private set; }

        public string? AfterTheInner { get; private set; }

        public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
        {
            using (OperationScope.Enter("App.Shipping.ReserveStockMutation"))
                Inside = OperationScope.Current;
            AfterTheInner = OperationScope.Current;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
            => throw new InvalidOperationException("Handler failed on purpose.");
    }
}
