using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Events;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     Verifies the AOT-safe typed event dispatch table wiring: every module with
///     <c>[EventHandler]</c>s emits a <c>GeneratedEventDispatchTable</c> switch and registers it
///     additively in <c>AddPragmaticEventHandlers()</c>, so <c>InMemoryEventDispatcher</c> routes
///     the untyped batch path through compile-time pattern matching instead of the dynamic fallback.
/// </summary>
public class EventDispatchTableGeneratorTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IDomainEvent>(),
        GeneratorTestHelper.FromType<Pragmatic.Events.Attributes.EventHandlerAttribute>()
    ];

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Events;
        using Pragmatic.Events.Attributes;

        namespace TestApp;

        public sealed record OrderPlaced(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;
        public sealed record OrderShipped(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;

        [EventHandler]
        public sealed class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default) => Task.CompletedTask;
        }

        [EventHandler]
        public sealed class OrderShippedHandler : IDomainEventHandler<OrderShipped>
        {
            public Task HandleAsync(OrderShipped @event, CancellationToken ct = default) => Task.CompletedTask;
        }
        """;

    private static string? GetDispatchTable(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Events.DispatchTable"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    private static string? GetRegistration(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("EventHandlerRegistration"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void EventHandlers_GenerateTypedDispatchTable_WithOneCasePerEvent()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var table = GetDispatchTable(result);

        table.Should().NotBeNull("a library with [EventHandler]s must emit a typed dispatch table");
        table!.Should()
            .Contain("class GeneratedEventDispatchTable : global::Pragmatic.Events.ITypedEventDispatchTable")
            .And.Contain("global::TestApp.OrderPlaced typed => dispatcher.DispatchAsync(typed, ct)")
            .And.Contain("global::TestApp.OrderShipped typed => dispatcher.DispatchAsync(typed, ct)")
            .And.Contain("_ => null");
    }

    [Fact]
    public void AddPragmaticEventHandlers_RegistersDispatchTableAdditively()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        var registration = GetRegistration(result);

        registration.Should().NotBeNull();
        // AddSingleton (not TryAdd/Replace): each composed module contributes its own table so
        // the dispatcher can probe all of them — cross-boundary events must not knock each other out.
        registration!.Should().Contain(
            "services.AddSingleton<global::Pragmatic.Events.ITypedEventDispatchTable, global::TestApp.Generated.GeneratedEventDispatchTable>();");
    }

    [Fact]
    public void MultipleHandlersForSameEvent_ProduceSingleCase_NoDuplicateLabels()
    {
        const string twoHandlersOneEvent = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Events;
            using Pragmatic.Events.Attributes;

            namespace TestApp;

            public sealed record OrderPlaced(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;

            [EventHandler]
            public sealed class AuditHandler : IDomainEventHandler<OrderPlaced>
            {
                public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default) => Task.CompletedTask;
            }

            [EventHandler]
            public sealed class NotifyHandler : IDomainEventHandler<OrderPlaced>
            {
                public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default) => Task.CompletedTask;
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(twoHandlersOneEvent, References);

        var table = GetDispatchTable(result);

        table.Should().NotBeNull();
        var occurrences = table!.Split("global::TestApp.OrderPlaced typed =>").Length - 1;
        occurrences.Should().Be(1, "duplicate case labels for the same event type would not compile");
    }
}
