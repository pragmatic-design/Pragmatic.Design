using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Pipeline;
using Xunit;

namespace Pragmatic.Events.Tests;

/// <summary>
///     Tests for <see cref="InMemoryEventDispatcher" /> behaviors not covered by
///     <see cref="InMemoryEventDispatcherTests" />: internal-call scoping via
///     <see cref="ICallContext" />, handler <c>Order</c> sorting, and the passthrough
///     dynamic-dispatch fallback of the enumerable overload.
/// </summary>
/// <remarks>
///     The handler fixtures here target a private <see cref="ScopeEvent" /> type (not
///     <c>TestDomainEvent</c>) so that the reflection-based assembly-scanning test
///     (<c>AddDomainEventHandlersFromAssembly_RegistersHandlersFromThisAssembly</c>), which
///     materialises every <c>IDomainEventHandler&lt;TestDomainEvent&gt;</c> via DI, never tries
///     to activate these constructor-injected helpers.
/// </remarks>
public class InMemoryEventDispatcherCallContextTests
{
    private sealed record ScopeEvent(string Message) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }

    // =========================================================================
    // ICallContext / EnterInternalCall
    // =========================================================================

    [Fact]
    public async Task DispatchAsync_WithCallContextRegistered_EntersInternalCallScopeAroundHandler()
    {
        var callContext = new RecordingCallContext();
        var handler = new InternalCallObservingHandler(callContext);
        var dispatcher = BuildDispatcher(services =>
        {
            services.AddSingleton<ICallContext>(callContext);
            services.AddSingleton<IDomainEventHandler<ScopeEvent>>(handler);
        });

        await dispatcher.DispatchAsync(new ScopeEvent("hi"));

        callContext.EnterCount.Should().Be(1);
        handler.WasInternalDuringHandle.Should().BeTrue("the handler must run inside an internal-call scope");
        callContext.IsInternalCall.Should().BeFalse("the scope must be disposed after the handler completes");
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_StillDisposesInternalCallScope()
    {
        var callContext = new RecordingCallContext();
        var dispatcher = BuildDispatcher(services =>
        {
            services.AddSingleton<ICallContext>(callContext);
            services.AddSingleton<IDomainEventHandler<ScopeEvent>>(new ThrowingScopeHandler());
        });

        // Failures are swallowed by the dispatcher; the scope must still be released.
        await dispatcher.DispatchAsync(new ScopeEvent("boom"));

        callContext.EnterCount.Should().Be(1);
        callContext.DisposeCount.Should().Be(1);
        callContext.IsInternalCall.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_WithoutCallContextRegistered_InvokesHandlerWithoutScoping()
    {
        var handler = new TestEventHandler();
        var dispatcher = BuildDispatcher(services =>
            services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(handler));

        // No ICallContext registered: dispatch must still succeed.
        await dispatcher.DispatchAsync(new TestDomainEvent("hi"));

        handler.HandledEvents.Should().ContainSingle();
    }

    // =========================================================================
    // Handler Order sorting
    // =========================================================================

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_InvokesInAscendingOrder()
    {
        var executionLog = new List<int>();
        var dispatcher = BuildDispatcher(services =>
        {
            // Registered out of order: 5, then 1, then 3.
            services.AddSingleton<IDomainEventHandler<ScopeEvent>>(new OrderedHandler(5, executionLog));
            services.AddSingleton<IDomainEventHandler<ScopeEvent>>(new OrderedHandler(1, executionLog));
            services.AddSingleton<IDomainEventHandler<ScopeEvent>>(new OrderedHandler(3, executionLog));
        });

        await dispatcher.DispatchAsync(new ScopeEvent("ordered"));

        executionLog.Should().Equal(1, 3, 5);
    }

    // =========================================================================
    // Passthrough dispatch-table fallback (enumerable overload, dynamic path)
    // =========================================================================

    [Fact]
    public async Task DispatchEnumerable_WithPassthroughDispatchTable_FallsBackToDynamicDispatch()
    {
        // The passthrough table always returns null from TryDispatch, forcing the
        // dispatcher down the dynamic (DLR) fallback path for each event.
        var testHandler = new TestEventHandler();
        var anotherHandler = new AnotherTestEventHandler();
        var dispatcher = BuildDispatcher(
            dispatchTable: new PassthroughEventDispatchTable(),
            configure: services =>
            {
                services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(testHandler);
                services.AddSingleton<IDomainEventHandler<AnotherTestEvent>>(anotherHandler);
            });

        var events = new IDomainEvent[]
        {
            new TestDomainEvent("a"),
            new AnotherTestEvent(7)
        };

        await dispatcher.DispatchAsync(events);

        testHandler.HandledEvents.Should().ContainSingle().Which.Message.Should().Be("a");
        anotherHandler.HandledEvents.Should().ContainSingle().Which.Value.Should().Be(7);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static InMemoryEventDispatcher BuildDispatcher(Action<IServiceCollection> configure)
        => BuildDispatcher(new PassthroughEventDispatchTable(), configure);

    private static InMemoryEventDispatcher BuildDispatcher(
        ITypedEventDispatchTable dispatchTable,
        Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var serviceProvider = services.BuildServiceProvider();
        return new InMemoryEventDispatcher(
            serviceProvider,
            NullLogger<InMemoryEventDispatcher>.Instance,
            [dispatchTable]);
    }

    private sealed class RecordingCallContext : ICallContext
    {
        public int EnterCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsInternalCall { get; private set; }

        public IDisposable EnterInternalCall()
        {
            EnterCount++;
            IsInternalCall = true;
            return new Scope(this);
        }

        private sealed class Scope(RecordingCallContext owner) : IDisposable
        {
            public void Dispose()
            {
                owner.DisposeCount++;
                owner.IsInternalCall = false;
            }
        }
    }

    private sealed class InternalCallObservingHandler(ICallContext callContext)
        : IDomainEventHandler<ScopeEvent>
    {
        public bool WasInternalDuringHandle { get; private set; }

        public Task HandleAsync(ScopeEvent @event, CancellationToken ct = default)
        {
            WasInternalDuringHandle = callContext.IsInternalCall;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingScopeHandler : IDomainEventHandler<ScopeEvent>
    {
        public Task HandleAsync(ScopeEvent @event, CancellationToken ct = default)
            => throw new InvalidOperationException("Handler failed on purpose.");
    }

    private sealed class OrderedHandler(int order, List<int> executionLog)
        : IDomainEventHandler<ScopeEvent>
    {
        public int Order => order;

        public Task HandleAsync(ScopeEvent @event, CancellationToken ct = default)
        {
            executionLog.Add(order);
            return Task.CompletedTask;
        }
    }
}
