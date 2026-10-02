using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for [EventHandler] attribute source generation.
/// </summary>
public class EventHandlerRegistrationTests
{
    [Fact]
    public void EventHandler_SingleHandler_GeneratesRegistration()
    {
        var source = """
                     using Pragmatic.Events;
                     using Pragmatic.Events.Attributes;

                     namespace TestApp.Events;

                     public sealed record OrderPlaced(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

                     [EventHandler]
                     public class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
                     {
                         public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
                             => Task.CompletedTask;
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Infra.Composition.EventHandlerRegistration.g.cs");
        var content = output["_Infra.Composition.EventHandlerRegistration.g.cs"];
        content.Should().Contain("AddPragmaticEventHandlers");
        content.Should().Contain(
            "AddScoped<global::Pragmatic.Events.IDomainEventHandler<global::TestApp.Events.OrderPlaced>, global::TestApp.Events.OrderPlacedHandler>");

        // The dispatcher the handlers need. Registering them without it is the shape this repository
        // keeps meeting: every [EventHandler] in DI, nothing to call them, the entity raises, the
        // invoker asks for an IDomainEventDispatcher, gets null, and the event goes nowhere in silence.
        // Found by wiring domain events in a consumer for the first time — the handlers were
        // registered, the interceptor raised, and no sweep ever ran.
        content.Should().Contain("AddInMemoryDomainEvents(services)");
    }

    [Fact]
    public void EventHandler_MissingInterface_DoesNotGenerateRegistration()
    {
        var source = """
                     using Pragmatic.Events.Attributes;

                     namespace TestApp;

                     [EventHandler]
                     public class NotAHandler { }
                     """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);

        output.Should().NotContainKey("Composition.TestApp.EventHandlerRegistration.g.cs");
    }

    [Fact]
    public void EventHandler_MultipleHandlers_GeneratesAll()
    {
        var source = """
                     using Pragmatic.Events;
                     using Pragmatic.Events.Attributes;

                     namespace TestApp.Events;

                     public sealed record OrderPlaced(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
                     public sealed record OrderCancelled(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

                     [EventHandler]
                     public class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
                     {
                         public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
                             => Task.CompletedTask;
                     }

                     [EventHandler]
                     public class OrderCancelledHandler : IDomainEventHandler<OrderCancelled>
                     {
                         public Task HandleAsync(OrderCancelled @event, CancellationToken ct = default)
                             => Task.CompletedTask;
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        var content = output["_Infra.Composition.EventHandlerRegistration.g.cs"];
        content.Should().Contain("OrderPlacedHandler");
        content.Should().Contain("OrderCancelledHandler");
    }
}
