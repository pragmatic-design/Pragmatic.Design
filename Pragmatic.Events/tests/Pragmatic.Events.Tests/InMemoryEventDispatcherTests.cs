using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests;

/// <summary>
///     Tests for <see cref="InMemoryEventDispatcher" />.
/// </summary>
public class InMemoryEventDispatcherTests
{
    private readonly ILogger<InMemoryEventDispatcher> _logger = NullLogger<InMemoryEventDispatcher>.Instance;
    private readonly ITypedEventDispatchTable[] _dispatchTables = [new TestEventDispatchTable()];

    /// <summary>
    ///     Test dispatch table that handles the test event types used in this test class.
    /// </summary>
    private sealed class TestEventDispatchTable : ITypedEventDispatchTable
    {
        public Task? TryDispatch(IDomainEventDispatcher dispatcher, IDomainEvent @event, CancellationToken ct)
            => @event switch
            {
                TestDomainEvent e => dispatcher.DispatchAsync(e, ct),
                AnotherTestEvent e => dispatcher.DispatchAsync(e, ct),
                _ => null
            };
    }

    // =========================================================================
    // Constructor
    // =========================================================================

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        var act = () => new InMemoryEventDispatcher(null!, _logger, _dispatchTables);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var sp = new ServiceProviderMock();

        var act = () => new InMemoryEventDispatcher(sp, null!, _dispatchTables);

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // DispatchAsync<TEvent> - Generic typed dispatch
    // =========================================================================

    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_InvokesHandler()
    {
        var handler = new TestEventHandler();
        var dispatcher = CreateDispatcher(handler);
        var @event = new TestDomainEvent("hello");

        await dispatcher.DispatchAsync(@event);

        handler.HandledEvents.Should().ContainSingle()
            .Which.Message.Should().Be("hello");
    }

    [Fact]
    public async Task DispatchAsync_WithNoHandlers_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();

        var act = () => dispatcher.DispatchAsync(new TestDomainEvent("hello"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_InvokesAll()
    {
        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        var dispatcher = CreateDispatcher(handler1, handler2);
        var @event = new TestDomainEvent("hello");

        await dispatcher.DispatchAsync(@event);

        handler1.HandledEvents.Should().ContainSingle();
        handler2.HandledEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerFails_DoesNotPropagateException()
    {
        // InMemoryEventDispatcher logs failures but does not propagate exceptions —
        // all handlers are invoked regardless of individual failures.
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(new FailingEventHandler());
        var sp = services.BuildServiceProvider();
        var dispatcher = new InMemoryEventDispatcher(sp, _logger, _dispatchTables);

        var act = () => dispatcher.DispatchAsync(new TestDomainEvent("hello"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerFails_ContinuesToInvokeSubsequentHandlers()
    {
        // InMemoryEventDispatcher continues the dispatch chain on failure —
        // subsequent handlers ARE invoked even when a previous handler throws.
        var failingHandler = new FailingEventHandler();
        var trackingHandler = new TestEventHandler();

        // Failing handler registered first
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(failingHandler);
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(trackingHandler);
        var sp = services.BuildServiceProvider();
        var dispatcher = new InMemoryEventDispatcher(sp, _logger, _dispatchTables);

        await dispatcher.DispatchAsync(new TestDomainEvent("hello"));

        // Subsequent handler was still invoked despite previous handler failure
        trackingHandler.HandledEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_PassesCancellationTokenToHandler()
    {
        var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        var handler = new DomainEventHandlerOfTestDomainEventMock();
        handler.HandleAsync.Returns((_, ct) =>
        {
            receivedToken = ct;
            return Task.CompletedTask;
        });

        var dispatcher = CreateDispatcherFromSubstitute(handler);

        await dispatcher.DispatchAsync(new TestDomainEvent("test"), cts.Token);

        receivedToken.Should().Be(cts.Token);
    }

    // =========================================================================
    // DispatchAsync(IEnumerable<IDomainEvent>) - Non-generic dispatch
    // =========================================================================

    [Fact]
    public async Task DispatchEnumerable_DispatchesAllEventsInOrder()
    {
        var handler = new TestEventHandler();
        var dispatcher = CreateDispatcher(handler);

        var events = new IDomainEvent[]
        {
            new TestDomainEvent("first"),
            new TestDomainEvent("second"),
            new TestDomainEvent("third")
        };

        await dispatcher.DispatchAsync(events);

        handler.HandledEvents.Should().HaveCount(3);
        handler.HandledEvents.Select(e => e.Message)
            .Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task DispatchEnumerable_WithEmptyList_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();

        var act = () => dispatcher.DispatchAsync(Enumerable.Empty<IDomainEvent>());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchEnumerable_WithMixedEventTypes_DispatchesEach()
    {
        var testHandler = new TestEventHandler();
        var anotherHandler = new AnotherTestEventHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(testHandler);
        services.AddSingleton<IDomainEventHandler<AnotherTestEvent>>(anotherHandler);
        var sp = services.BuildServiceProvider();
        var dispatcher = new InMemoryEventDispatcher(sp, _logger, _dispatchTables);

        var events = new IDomainEvent[]
        {
            new TestDomainEvent("hello"),
            new AnotherTestEvent(42),
            new TestDomainEvent("world")
        };

        await dispatcher.DispatchAsync(events);

        testHandler.HandledEvents.Should().HaveCount(2);
        anotherHandler.HandledEvents.Should().ContainSingle()
            .Which.Value.Should().Be(42);
    }

    [Fact]
    public async Task DispatchEnumerable_RespectsCancellation()
    {
        var handler = new TestEventHandler();
        var dispatcher = CreateDispatcher(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var events = new IDomainEvent[] { new TestDomainEvent("should not dispatch") };
        var act = () => dispatcher.DispatchAsync(events, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DispatchEnumerable_WhenHandlerFails_ContinuesProcessingRemainingEvents()
    {
        // InMemoryEventDispatcher swallows individual handler failures and continues
        // dispatching the remaining events in the enumerable.
        var failingHandler = new FailingEventHandler();
        var anotherHandler = new AnotherTestEventHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(failingHandler);
        services.AddSingleton<IDomainEventHandler<AnotherTestEvent>>(anotherHandler);
        var sp = services.BuildServiceProvider();
        var dispatcher = new InMemoryEventDispatcher(sp, _logger, _dispatchTables);

        var events = new IDomainEvent[]
        {
            new TestDomainEvent("will fail"),
            new AnotherTestEvent(42) // should still be dispatched
        };

        await dispatcher.DispatchAsync(events);

        // Second event was dispatched despite first event's handler failing
        anotherHandler.HandledEvents.Should().ContainSingle();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private InMemoryEventDispatcher CreateDispatcher(params TestEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
            services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(handler);

        var sp = services.BuildServiceProvider();
        return new InMemoryEventDispatcher(sp, _logger, _dispatchTables);
    }

    private InMemoryEventDispatcher CreateDispatcherFromSubstitute(
        IDomainEventHandler<TestDomainEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();
        return new InMemoryEventDispatcher(sp, _logger, _dispatchTables);
    }
}
