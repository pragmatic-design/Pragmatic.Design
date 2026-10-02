using Microsoft.CodeAnalysis;
using Pragmatic.Events;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A class that handles several domain events is registered for <b>every</b> one of them.
/// </summary>
/// <remarks>
///     <para>
///         <c>EventHandlerTransform</c> reads <b>every</b> <c>IDomainEventHandler&lt;T&gt;</c> a class
///         implements. Reading only the first would make the registration name one interface and leave
///         the others silently absent: valid C#, a green build, and a handler that never runs for the
///         events it was written to handle.
///     </para>
///     <para>
///         ⚠️ Invoicing has that shape: one class records an invoice's three moves in the audit trail,
///         and the company's two. Registering the first interface only leaves four of five
///         events with no handler, while <c>grep</c> shows a handler for each.
///     </para>
///     <para>
///         ⚠️ And it looks like something else entirely: the events are raised inside a
///         <c>[DomainAction]</c>, so a diagnosis naturally goes after the dispatch — the unit of work,
///         the commit scope, the deferred flush — downstream of a cause that is upstream, in the
///         registration.
///     </para>
/// </remarks>
public class OneHandlerForSeveralEventsTests
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

        public sealed record InvoiceIssued(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;
        public sealed record InvoicePaid(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;
        public sealed record InvoiceVoided(Guid Id, DateTimeOffset OccurredAt) : IDomainEvent;

        /// <summary>The three moves differ by a name; three near-identical classes would be three
        /// places to change the day the trail gains a field.</summary>
        [EventHandler]
        public sealed class RecordWhatTheMoveMeant
            : IDomainEventHandler<InvoiceIssued>,
                IDomainEventHandler<InvoicePaid>,
                IDomainEventHandler<InvoiceVoided>
        {
            public Task HandleAsync(InvoiceIssued @event, CancellationToken ct = default) => Task.CompletedTask;
            public Task HandleAsync(InvoicePaid @event, CancellationToken ct = default) => Task.CompletedTask;
            public Task HandleAsync(InvoiceVoided @event, CancellationToken ct = default) => Task.CompletedTask;
        }

        /// <summary>The control: one class, one event, which must keep working exactly as before.</summary>
        [EventHandler]
        public sealed class InviteTheCustomer : IDomainEventHandler<InvoiceIssued>
        {
            public Task HandleAsync(InvoiceIssued @event, CancellationToken ct = default) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void AClassHandlingThreeEvents_IsRegisteredForAllThree()
    {
        var registration = Registration();

        foreach (var @event in new[] { "InvoiceIssued", "InvoicePaid", "InvoiceVoided" })
        {
            registration.Should().Contain(
                $"AddScoped<global::Pragmatic.Events.IDomainEventHandler<global::TestApp.{@event}>, "
                + "global::TestApp.RecordWhatTheMoveMeant>()",
                $"the class handles {@event}, so something must hand it one");
        }
    }

    /// <summary>The control: a class that handles one event is registered once, as it always was.</summary>
    [Fact]
    public void AClassHandlingOneEvent_IsRegisteredOnce()
    {
        var registration = Registration();

        CountOf(registration, "global::TestApp.InviteTheCustomer>()").Should().Be(1,
            "one interface, one registration — no duplicate from the new path");
    }

    /// <summary>
    ///     The dispatch table routes every handled event, not just the first of each class.
    /// </summary>
    /// <remarks>
    ///     The untyped batch path goes through this switch, so an event missing from it falls back to
    ///     the dynamic route — which is the AOT-unsafe one the table exists to avoid.
    /// </remarks>
    [Fact]
    public void TheDispatchTable_RoutesEveryHandledEvent()
    {
        var table = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run())
            .Where(kv => kv.Key.Contains("Events.DispatchTable"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        table.Should().NotBeNull();
        foreach (var @event in new[] { "InvoiceIssued", "InvoicePaid", "InvoiceVoided" })
            table!.Should().Contain($"global::TestApp.{@event} typed", $"{@event} has a handler");
    }

    /// <summary>The metadata the host reads names every handled event too.</summary>
    /// <remarks>
    ///     It is how a host discovers what to call: an event left out of it is one the host never
    ///     wires, which is the same silence one level up.
    /// </remarks>
    [Fact]
    public void TheMetadata_NamesEveryHandledEvent()
    {
        var metadata = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run())
            .Where(kv => kv.Key.Contains("Metadata.EventHandlers"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        metadata.Should().NotBeNull();
        foreach (var @event in new[] { "InvoiceIssued", "InvoicePaid", "InvoiceVoided" })
            metadata!.Should().Contain($"global::TestApp.{@event}");
    }

    private static SourceGenRunResult Run()
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

    private static string Registration()
    {
        var registration = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run())
            .Where(kv => kv.Key.Contains("EventHandlerRegistration"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        registration.Should().NotBeNull("a module with [EventHandler]s registers them");
        return registration!;
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, System.StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal))
            count++;

        return count;
    }
}
