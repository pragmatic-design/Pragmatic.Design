using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Attributes;
using Pragmatic.Events;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Caching;

/// <summary>
///     <c>[InvalidatesCache]</c> says it marks a domain event and that the generator creates the
///     event handler which invalidates the declared tags/keys. That generated handler is the only
///     thing that dispatches an <c>ICacheInvalidator</c> for an <see cref="IDomainEvent"/> — the
///     only other automatic consumer is the mutation invoker — so without it the attribute would be
///     inert on events.
///     These tests assert on the generated code: the handler exists, is a real
///     <c>IDomainEventHandler&lt;TEvent&gt;</c>, and is registered on the dispatcher.
/// </summary>
public class CacheInvalidationEventHandlerTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IDomainEvent>(),
        GeneratorTestHelper.FromType<InvalidatesCacheAttribute>(),
        GeneratorTestHelper.FromType<ITypedEventDispatchTable>(),
        GeneratorTestHelper.FromType<IServiceCollection>()
    ];

    private const string EventSource = """
        using System;
        using Pragmatic.Caching.Attributes;
        using Pragmatic.Events;

        namespace TestApp;

        [InvalidatesCache("invoices", "tenant:{TenantId}", Keys = new[] { "invoice:{Id}" })]
        public sealed record InvoicePaid(Guid Id, int TenantId, DateTimeOffset OccurredAt) : IDomainEvent;
        """;

    private static string? GetFile(SourceGenRunResult result, string hintFragment)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(hintFragment))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void InvalidatesCache_OnDomainEvent_GeneratesDomainEventHandler()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        var handler = GetFile(result, "InvoicePaid.CacheInvalidationHandler");

        handler.Should().NotBeNull(
            "the attribute promises an event handler; without one [InvalidatesCache] on an event does nothing");
        handler!.Should()
            .Contain(
                "class InvoicePaidCacheInvalidationHandler : global::Pragmatic.Events.IDomainEventHandler<global::TestApp.InvoicePaid>")
            .And.Contain("HandleAsync");
    }

    [Fact]
    public void InvalidatesCache_OnDomainEvent_HandlerInvalidatesDeclaredTagsAndKeys()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        var handler = GetFile(result, "InvoicePaid.CacheInvalidationHandler");

        handler.Should().NotBeNull();
        handler!.Should()
            .Contain("\"invoices\"")
            // Placeholders resolve against the dispatched event instance, not against `this`:
            // the handler is a separate object from the event.
            .And.Contain("$\"tenant:{@event.TenantId}\"")
            .And.Contain("$\"invoice:{@event.Id}\"");
    }

    [Fact]
    public void InvalidatesCache_OnDomainEvent_RegistersHandlerOnTheDispatcher()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        var registration = GetFile(result, "EventHandlerRegistration");

        registration.Should().NotBeNull(
            "a handler nobody registers is never invoked — the defect is the missing bridge, not the missing class");
        registration!.Should().Contain(
            "services.AddScoped<global::Pragmatic.Events.IDomainEventHandler<global::TestApp.InvoicePaid>, global::TestApp.InvoicePaidCacheInvalidationHandler>();");
    }

    [Fact]
    public void InvalidatesCache_OnDomainEvent_AddsEventToTypedDispatchTable()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        var table = GetFile(result, "Events.DispatchTable");

        table.Should().NotBeNull("the AOT-safe untyped dispatch path routes through the generated table");
        table!.Should().Contain("global::TestApp.InvoicePaid typed => dispatcher.DispatchAsync(typed, ct)");
    }

    [Fact]
    public void InvalidatesCache_OnDomainEvent_GeneratedCodeCompiles()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void InvalidatesCache_OnNonPartialDomainEvent_DoesNotRequirePartial()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(EventSource, References);

        // PRAG1704 exists for the partial the mutation path needs. The handler path generates a
        // separate type, so a plain `public sealed record X(...) : IDomainEvent;` must be accepted.
        GeneratorTestHelper.HasDiagnostic(result, "PRAG1704").Should().BeFalse();
    }

    [Fact]
    public void InvalidatesCache_OnPartialDomainEvent_KeepsTheInvalidatorPartialToo()
    {
        // Additive on purpose. An event already declared partial may have code calling InvalidateAsync
        // by hand — the samples do exactly that — and dropping the ICacheInvalidator implementation
        // would stop it compiling. It is inert on the dispatch path; the handler is what runs.
        const string partialEvent = """
            using System;
            using Pragmatic.Caching.Attributes;
            using Pragmatic.Events;

            namespace TestApp;

            [InvalidatesCache("invoices")]
            public sealed partial record InvoiceVoided(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(partialEvent, References);

        GetFile(result, "InvoiceVoided.CacheInvalidationHandler").Should().NotBeNull();
        GetFile(result, "InvoiceVoided.CacheInvalidator").Should().NotBeNull();
        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void InvalidatesCache_OnNonEvent_KeepsThePartialInvalidator_AndGeneratesNoHandler()
    {
        const string mutationSource = """
            using Pragmatic.Caching.Attributes;

            namespace TestApp;

            [InvalidatesCache("users")]
            public partial class UpdateUserMutation
            {
                public int UserId { get; init; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(mutationSource, References);

        var invalidator = GetFile(result, "UpdateUserMutation.CacheInvalidator");
        invalidator.Should().NotBeNull("the mutation invoker consumes ICacheInvalidator on the mutation itself");
        invalidator!.Should().Contain("global::Pragmatic.Caching.ICacheInvalidator");

        GetFile(result, "CacheInvalidationHandler").Should().BeNull(
            "a mutation is not dispatched as a domain event; generating a handler for it would invalidate twice");
    }
}
