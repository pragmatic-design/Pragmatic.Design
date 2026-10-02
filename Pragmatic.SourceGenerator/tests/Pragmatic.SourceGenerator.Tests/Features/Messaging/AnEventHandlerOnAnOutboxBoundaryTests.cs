using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     <c>[EnableOutbox]</c> on a boundary means every <c>[EventHandler]</c> of that
///     boundary never runs, and now the build says so.
/// </summary>
/// <remarks>
///     <para>
///         The two halves are individually right and together silent. <c>OutboxInterceptor</c> calls
///         <c>ClearDomainEvents()</c> while <c>SavingChanges</c> is in flight — it has to, or the same
///         event would be both a row and an in-process dispatch — and <c>EfCoreUnitOfWork</c> takes the
///         events <b>after</b> the commit, which is also deliberate: an event announcing a write that
///         failed is worse than one never sent. By the time it takes them, there are none.
///     </para>
///     <para>
///         So the handler stays registered and is never entered: no log, no dead letter, nothing.
///         ⚠️ <b>The upgrade path is the dangerous one</b> — an application with working
///         <c>[EventHandler]</c>s that adds <c>[EnableOutbox]</c>, for the good reason that its
///         integration events should be transactional, loses every in-process handler at that moment
///         with a green build and a green suite.
///     </para>
///     <para>
///         What runs on such a boundary is <c>[MessageHandler] IMessageHandler&lt;T&gt;</c>: what
///         arrives is the message the pump publishes from the outbox row. The diagnostic names it —
///         changing the behaviour instead (dispatching locally <em>and</em> publishing) is a product
///         decision left open on the issue, and it would make an event run twice in a service that
///         consumes its own topic.
///     </para>
/// </remarks>
public class AnEventHandlerOnAnOutboxBoundaryTests
{
    private const string Diagnostic = "PRAG0837";

    /// <summary>
    ///     The framework's shapes as it declares them: the attributes by their metadata names, because
    ///     that is what the generator's pipelines match on.
    /// </summary>
    private const string Stubs = """
        namespace Pragmatic.Events
        {
            public interface IDomainEvent { }
            public interface IDomainEventHandler<TEvent> where TEvent : IDomainEvent
            {
                System.Threading.Tasks.Task HandleAsync(TEvent e, System.Threading.CancellationToken ct);
            }
        }

        namespace Pragmatic.Events.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EventHandlerAttribute : System.Attribute { }
        }

        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed class MessageContext { }
            public interface IMessageHandler<TMessage>
            {
                System.Threading.Tasks.Task HandleAsync(TMessage m, MessageContext c, System.Threading.CancellationToken ct);
            }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EnableOutboxAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { }
        }

        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BoundaryAttribute : System.Attribute { }
        }

        namespace Pragmatic.Composition.Attributes
        {
            // What makes a compilation a Pragmatic module as far as the generator is concerned
            // (CompositionDetector), and therefore what makes it register event handlers at all.
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute { }
        }
        """;

    private const string TheEvent = """
        namespace App.Intake.Events
        {
            public sealed record CaseDecided(System.Guid CaseId) : Pragmatic.Events.IDomainEvent;
        }
        """;

    private static string Module(string boundaryAttributes, string handler) => $$"""
        {{Stubs}}

        {{TheEvent}}

        namespace App.Intake
        {
            {{boundaryAttributes}}
            public partial class IntakeBoundary { }
        }

        namespace App.Intake.Infrastructure
        {
            {{handler}}
        }
        """;

    private const string AnEventHandler = """
        [Pragmatic.Events.Attributes.EventHandler]
        public sealed class TellTheApplicant : Pragmatic.Events.IDomainEventHandler<App.Intake.Events.CaseDecided>
        {
            public System.Threading.Tasks.Task HandleAsync(
                App.Intake.Events.CaseDecided e, System.Threading.CancellationToken ct)
                => System.Threading.Tasks.Task.CompletedTask;
        }
        """;

    private const string AMessageHandler = """
        [Pragmatic.Messaging.Attributes.MessageHandler]
        public sealed class TellTheApplicant : Pragmatic.Messaging.IMessageHandler<App.Intake.Events.CaseDecided>
        {
            public System.Threading.Tasks.Task HandleAsync(
                App.Intake.Events.CaseDecided m, Pragmatic.Messaging.MessageContext c,
                System.Threading.CancellationToken ct)
                => System.Threading.Tasks.Task.CompletedTask;
        }
        """;

    /// <summary>The setpoint: the silence this story exists to close.</summary>
    [Fact]
    public void AnEventHandlerOfAnOutboxBoundary_IsReported()
    {
        var ids = IdsOf(Module(
            "[Pragmatic.Actions.Attributes.Boundary]\n    [Pragmatic.Messaging.Attributes.EnableOutbox]",
            AnEventHandler));

        ids.Should().Contain(Diagnostic,
            "the handler is registered and never entered, and nothing said so");
    }

    /// <summary>
    ///     The control that matters most: without the outbox the handler runs, and a diagnostic there
    ///     would fire on the ordinary shape.
    /// </summary>
    [Fact]
    public void AnEventHandlerOfAnOrdinaryBoundary_IsNotReported()
    {
        IdsOf(Module("[Pragmatic.Actions.Attributes.Boundary]", AnEventHandler))
            .Should().NotContain(Diagnostic,
                "an in-process domain event handler is the normal shape and it works");
    }

    /// <summary>And the form the message names is not reported either.</summary>
    [Fact]
    public void AMessageHandlerOfAnOutboxBoundary_IsNotReported()
    {
        IdsOf(Module(
                "[Pragmatic.Actions.Attributes.Boundary]\n    [Pragmatic.Messaging.Attributes.EnableOutbox]",
                AMessageHandler))
            .Should().NotContain(Diagnostic,
                "what arrives on an outbox boundary is the message, and this is the handler for it");
    }

    /// <summary>
    ///     The message has to be actionable: it names the handler, the boundary and the attribute that
    ///     works.
    /// </summary>
    [Fact]
    public void TheMessage_NamesTheHandlerTheBoundaryAndWhatToWriteInstead()
    {
        var message = GeneratorTestHelper
            .RunGenerator<PragmaticSourceGenerator>(
                Module(
                    "[Pragmatic.Actions.Attributes.Boundary]\n    [Pragmatic.Messaging.Attributes.EnableOutbox]",
                    AnEventHandler),
                [])
            .Diagnostics.First(d => d.Id == Diagnostic)
            .GetMessage();

        message.Should().Contain("TellTheApplicant");
        message.Should().Contain("IntakeBoundary");
        message.Should().Contain("MessageHandler",
            "the remedy is the form that runs, and a diagnostic that only says 'this does nothing' "
            + "leaves the reader where the silence did");
    }

    private static string[] IdsOf(string source)
        => [.. GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, [])
            .Diagnostics.Select(d => d.Id).Distinct()];
}
