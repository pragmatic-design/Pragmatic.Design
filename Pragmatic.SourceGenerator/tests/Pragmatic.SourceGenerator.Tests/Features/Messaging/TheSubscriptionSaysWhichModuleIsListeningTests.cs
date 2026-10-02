using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Every subscription this assembly registers says <b>which module</b> is listening, so two
///     services consuming one event each get a copy instead of dividing the messages.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A subscription name built entirely at runtime, from the transport and the message type,
///         is <c>rabbitmq-VerificationRequested</c> for every listener. On RabbitMQ the subscription
///         name <em>is</em> the queue name, so Casework's two services — Intake's saga and Verify's
///         handler, both subscribing to one event — would declare the same queue on the same broker and
///         become competing consumers on it: each request reaches one service and never both.
///     </para>
///     <para>
///         Nothing the binder can see at runtime answers "who am I" — a
///         <see cref="Pragmatic.Messaging.MessageSubscription" /> is one marker per message type — so
///         the module's name has to be written into the registration here, where it is known.
///     </para>
/// </remarks>
public class TheSubscriptionSaysWhichModuleIsListeningTests
{
    private static MessageHandlerModel Handler(
        string assembly, string ns, string typeName, string messageFqn, string? bus = null) => new()
    {
        Namespace = ns,
        TypeName = typeName,
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        AssemblyName = assembly,
        MessageTypeFqn = messageFqn,
        MessageTypeShortName = messageFqn.Substring(messageFqn.LastIndexOf('.') + 1),
        BusName = bus,
    };

    private static string Render(params MessageHandlerModel[] handlers)
        => new HandlerRegistrationTemplate(ImmutableArray.Create(handlers)).RenderOutput().Text;

    /// <summary>The setpoint: the registration carries the module's own name.</summary>
    [Fact]
    public void TheSubscription_NamesTheModuleThatRegisteredIt()
    {
        var source = Render(Handler(
            "Casework.Verify",
            "Casework.Verify.Infrastructure.MessageHandlers",
            "RecordTheVerificationRequest",
            "global::Casework.Intake.Events.VerificationRequested"));

        source.Should().Contain(
            "AddMessageSubscription<global::Casework.Intake.Events.VerificationRequested>(services, \"verify\")",
            "the subscriber is the module listening, not the module that published the event");
    }

    /// <summary>
    ///     And two modules consuming the same event register different subscribers — which is the
    ///     measured failure.
    /// </summary>
    /// <remarks>
    ///     The event is <c>Casework.Intake.Events.VerificationRequested</c> in both, on purpose: the
    ///     name must come from the <b>consumer</b>, and a name derived from the message type would be
    ///     identical here, which is exactly what it was.
    /// </remarks>
    [Fact]
    public void TwoModulesConsumingOneEvent_RegisterDifferentSubscribers()
    {
        const string theEvent = "global::Casework.Intake.Events.VerificationRequested";

        var verify = Render(Handler(
            "Casework.Verify", "Casework.Verify.Infrastructure.MessageHandlers", "RecordIt", theEvent));
        var intake = Render(Handler(
            "Casework.Intake", "Casework.Intake.Infrastructure.Sagas", "CarryTheCase", theEvent));

        verify.Should().Contain("(services, \"verify\")");
        intake.Should().Contain("(services, \"intake\")");
    }

    /// <summary>
    ///     ⚠️ The control that decided where the name is read from: it is <b>not</b> the handler's
    ///     folder.
    /// </summary>
    /// <remarks>
    ///     A first version read the boundary out of the handler's namespace. For a handler in
    ///     <c>MyApp.Handlers</c> that produced the subscriber <c>"handlers"</c> — a consumer group named
    ///     after a convention rather than after anyone, which identifies nothing and would collide with
    ///     every other module that also keeps its handlers in a folder called that. Found by diffing a
    ///     snapshot, not by reasoning. The assembly is the module's own name by construction.
    /// </remarks>
    [Fact]
    public void TheSubscriber_IsNotTheNameOfTheHandlersFolder()
    {
        var source = Render(Handler(
            "MyApp", "MyApp.Handlers", "OrderPlacedHandler", "global::MyApp.Events.OrderPlaced"));

        source.Should().NotContain("(services, \"handlers\")",
            "a folder every module has is not an identity");
        source.Should().Contain("(services, \"my-app\")");
    }

    /// <summary>
    ///     Handlers of one message type in two boundaries of one module share one subscription, under
    ///     one name.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the case the decision on this issue said to measure before choosing the rule, and
    ///     it is why the name is not per boundary. <c>TransportSubscriptionBinder</c> deduplicates by
    ///     message type and subscribes <b>once</b>, then dispatches in process to every handler of that
    ///     type. Two names here would mean two queues bound to one topic, two copies delivered into one
    ///     process, and <b>every handler running twice</b> — worse than the defect this issue reports.
    /// </remarks>
    [Fact]
    public void HandlersOfOneTypeInTwoBoundaries_ShareOneSubscriptionAndOneName()
    {
        const string theEvent = "global::Shop.Events.OrderPlaced";

        var source = Render(
            Handler("Shop.Sales", "Shop.Sales.Invoicing.Handlers", "RaiseTheInvoice", theEvent),
            Handler("Shop.Sales", "Shop.Sales.Shipping.Handlers", "BookThePickup", theEvent));

        var registrations = source
            .Split('\n')
            .Count(line => line.Contains("AddMessageSubscription<", StringComparison.Ordinal));

        registrations.Should().Be(1,
            "one subscription per message type: two would deliver two copies and run both handlers twice");
        source.Should().Contain("(services, \"sales\")");
    }

    /// <summary>
    ///     ⚠️ A model with no assembly name falls back to the namespace rather than emitting an empty
    ///     subscriber.
    /// </summary>
    /// <remarks>
    ///     Found by reading an accepted snapshot: a fixture that sets no <c>AssemblyName</c> produced
    ///     <c>(services, "")</c>. That does not fail here — it fails at startup, in the binder, about a
    ///     subscription declared three files away — which is the shape of defect this repository has a
    ///     name for. A real compilation always has both, so this is the fixture case; the fallback is
    ///     the same one <c>NamespaceFor</c> uses, so the two agree about what an assembly-less model
    ///     means.
    /// </remarks>
    [Fact]
    public void AModelWithNoAssemblyName_FallsBackToTheNamespace()
    {
        var source = Render(new MessageHandlerModel
        {
            Namespace = "MyApp.Billing",
            TypeName = "InvoicePaidHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            MessageTypeFqn = "global::MyApp.Billing.Events.InvoicePaid",
            MessageTypeShortName = "InvoicePaid",
        });

        source.Should().NotContain("(services, \"\")", "an empty subscriber fails far from here, not here");
        source.Should().Contain("(services, \"billing\")");
    }

    /// <summary>A named bus keeps its own registration, and now carries the subscriber too.</summary>
    /// <remarks>
    ///     The overload is named rather than overloaded on a second string: with an optional
    ///     <c>subscriber</c> on the plain method, the old call
    ///     <c>AddMessageSubscription&lt;T&gt;(services, "analytics")</c> would have bound the bus name to
    ///     the subscriber and compiled — a silent change of meaning in generated code nobody reads.
    /// </remarks>
    [Fact]
    public void ASubscriptionOnANamedBus_CarriesBothTheBusAndTheSubscriber()
    {
        var source = Render(Handler(
            "MyApp", "MyApp.Analytics", "PageViewHandler", "global::MyApp.Events.PageViewed", bus: "analytics"));

        source.Should().Contain(
            "AddMessageSubscriptionOnBus<global::MyApp.Events.PageViewed>(services, \"analytics\", \"my-app\")");
    }
}
