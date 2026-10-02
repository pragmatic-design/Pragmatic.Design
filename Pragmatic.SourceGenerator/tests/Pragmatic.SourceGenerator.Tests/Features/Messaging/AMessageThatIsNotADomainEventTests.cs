using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     A <c>[MessageHandler]</c> whose message is <b>not</b> a domain event compiles.
/// </summary>
/// <remarks>
///     <para>
///         The registration emits a bridge that lets a domain event dispatched in process also reach a
///         message handler: <c>IDomainEventHandler&lt;T&gt;</c> implemented by
///         <c>MessageHandlerEventAdapter&lt;T&gt;</c>, both constrained to <c>IDomainEvent</c>. It is
///         emitted only for messages that are domain events: emitted for <b>every</b> message type, a
///         cross-service contract — a record, published to a broker, with no reason to be a domain
///         event — would not compile, and the error would be inside a generated file its author cannot
///         touch.
///     </para>
///     <para>
///         Casework is the application in this repository whose message crosses a process boundary.
///         The Showcase publishes from one boundary to another inside one process, where every message
///         is also a domain event — which is why that application alone does not exercise this shape.
///     </para>
/// </remarks>
public class AMessageThatIsNotADomainEventTests
{
    private const string Stubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
            public sealed class MessageHandlerEventAdapter<TEvent> : global::Pragmatic.Events.IDomainEventHandler<TEvent>
                where TEvent : global::Pragmatic.Events.IDomainEvent
            {
            }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }
        }

        namespace Pragmatic.Events
        {
            public interface IDomainEvent { }
            public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent { }
        }
        """;

    /// <summary>
    ///     Two handlers in one assembly: one message is a domain event, the other is only a message. The
    ///     pair is the point — a rule that emitted the bridge for neither would also make the second test
    ///     pass.
    /// </summary>
    private const string TwoHandlers = """

        namespace MyApp
        {
            public sealed record OrderPlaced(System.Guid Id) : Pragmatic.Events.IDomainEvent;

            public sealed record ShipmentRequested(System.Guid Id);

            [Pragmatic.Messaging.Attributes.MessageHandler]
            public partial class WhenAnOrderIsPlaced : Pragmatic.Messaging.IMessageHandler<OrderPlaced>
            {
                public System.Threading.Tasks.Task HandleAsync(OrderPlaced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                    => System.Threading.Tasks.Task.CompletedTask;
            }

            [Pragmatic.Messaging.Attributes.MessageHandler]
            public partial class WhenAShipmentIsRequested : Pragmatic.Messaging.IMessageHandler<ShipmentRequested>
            {
                public System.Threading.Tasks.Task HandleAsync(ShipmentRequested message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                    => System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public void TheBridge_IsNotEmitted_ForAMessageThatIsNotADomainEvent()
    {
        var registration = Registration();

        registration.Should().NotContain(
            "IDomainEventHandler<global::MyApp.ShipmentRequested>",
            "nothing dispatches a plain message as a domain event, so the bridge would only be a type error");
    }

    /// <summary>The control: the bridge a domain event does need is still there.</summary>
    [Fact]
    public void TheBridge_IsStillEmitted_ForAMessageThatIsADomainEvent()
    {
        var registration = Registration();

        registration.Should().Contain(
            "IDomainEventHandler<global::MyApp.OrderPlaced>",
            "a domain event dispatched in process still has to reach its message handler");
    }

    /// <summary>
    ///     And the whole reason this matters: no constraint is violated in the generated code. Before the
    ///     fix this compilation carried two <c>CS0311</c> — <c>ShipmentRequested</c> used where
    ///     <c>IDomainEvent</c> was required — inside a file the author cannot edit.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It asserts the absence of <c>CS0311</c> and not "no errors at all", deliberately: this
    ///     compilation is stubs with no references, so the generated registration also cannot see
    ///     <c>Microsoft.Extensions.DependencyInjection</c>. A blanket assertion would be red for a reason
    ///     that has nothing to do with this defect, which is how a test starts being ignored.
    /// </remarks>
    [Fact]
    public void TheGeneratedCode_ViolatesNoConstraint()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + TwoHandlers, []);

        var constraintViolations = result.OutputCompilation
            .GetDiagnostics()
            .Where(d => d.Id == "CS0311")
            .Select(d => d.ToString())
            .ToList();

        constraintViolations.Should().BeEmpty(string.Join("; ", constraintViolations));
    }

    private static string Registration()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + TwoHandlers, []);

        return GeneratorTestHelper.GetGeneratedSource(result, "_Infra.Messaging.Registration.g.cs")
               ?? throw new Xunit.Sdk.XunitException(
                   "no registration was generated at all, which is a different defect from the one this asserts");
    }
}
