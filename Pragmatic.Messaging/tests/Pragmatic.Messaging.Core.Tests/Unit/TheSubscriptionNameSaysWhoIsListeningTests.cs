using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     A subscription's name identifies <b>who is listening</b>, so two services consuming one event
///     each get their own queue instead of dividing the messages between them.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A name like <c>$"{transport.Name}-{messageType.Name}"</c> carries the transport and the
///         message and <b>nothing of the subscriber</b> — and on RabbitMQ the subscription name
///         <em>is</em> the queue name. A saga in one service and a handler in another, both subscribed
///         to <c>VerificationRequested</c>, would both declare <c>rabbitmq-VerificationRequested</c> on
///         the same broker, become competing consumers on one queue, and each request would reach one
///         service and never both.
///     </para>
///     <para>
///         The opposite of what a topic is for: a topic exists so that N subscribers each get a copy.
///     </para>
/// </remarks>
public class TheSubscriptionNameSaysWhoIsListeningTests
{
    private sealed record VerificationRequested(string CaseId);

    /// <summary>The setpoint, and it is the measured failure.</summary>
    [Fact]
    public void TwoServicesConsumingOneEvent_GetTheirOwnNames()
    {
        var intake = SubscriptionName.For("intake", typeof(VerificationRequested), busName: null);
        var verify = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);

        intake.Should().NotBe(verify,
            "two services consuming one event each get a copy; one queue makes them divide the messages");
    }

    /// <summary>And the name says which message, so one service's two subscriptions do not collide.</summary>
    /// <remarks>
    ///     The control on the case above: two <em>different</em> names is also satisfied by a name that
    ///     is only the subscriber, which would then put every message type of a service on one queue.
    /// </remarks>
    [Fact]
    public void TheNameCarriesBothTheSubscriberAndTheMessage()
    {
        var name = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);

        name.Should().Contain("verify").And.Contain("verification-requested");
    }

    /// <summary>
    ///     ⚠️ The name does <b>not</b> depend on the transport, and that is deliberate.
    /// </summary>
    /// <remarks>
    ///     The old name led with <c>rabbitmq-</c>, which reads like a namespace and is not one: a broker
    ///     already is the transport, so the segment distinguished nothing while the subscriber — the
    ///     thing that actually differs between two queues on that broker — was absent.
    /// </remarks>
    [Fact]
    public void TheNameIsNotTheTransport()
    {
        var name = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);

        name.Should().NotContain("rabbitmq").And.NotContain("kafka");
    }

    /// <summary>
    ///     Two named buses in one service get their own names too.
    /// </summary>
    /// <remarks>
    ///     One consumer service runs per bus and each binds only its own subscriptions, so without the
    ///     bus in the name two buses backed by the same broker would compute one name for one message
    ///     type and compete — the same defect one level down.
    /// </remarks>
    [Fact]
    public void TwoNamedBusesInOneService_GetTheirOwnNames()
    {
        var integration = SubscriptionName.For("verify", typeof(VerificationRequested), "integration");
        var analytics = SubscriptionName.For("verify", typeof(VerificationRequested), "analytics");

        integration.Should().NotBe(analytics);
        integration.Should().Contain("integration");
    }

    /// <summary>The default bus adds nothing, so its name stays the short one.</summary>
    /// <remarks>
    ///     The control on the case above: putting a placeholder in for the default bus would make every
    ///     ordinary queue carry a segment that never varies.
    /// </remarks>
    [Fact]
    public void TheDefaultBus_AddsNothingToTheName()
    {
        var name = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);

        name.Should().Be("verify.verification-requested");
    }

    /// <summary>
    ///     ⚠️ The same inputs always give the same name — it is what makes the queue survive a restart.
    /// </summary>
    /// <remarks>
    ///     A queue name is operational state: a name that varied with handler discovery order, or with
    ///     anything else about the process, would leave the previous queue bound and holding messages on
    ///     every deployment. This is asserted rather than assumed because the subscriber is computed
    ///     from a set of handler namespaces upstream, and a set has no order.
    /// </remarks>
    [Fact]
    public void TheNameIsStable()
    {
        var first = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);
        var second = SubscriptionName.For("verify", typeof(VerificationRequested), busName: null);

        second.Should().Be(first);
    }

    /// <summary>A subscriber nobody supplied is refused rather than silently named after nothing.</summary>
    /// <remarks>
    ///     An empty subscriber collapses every service back onto one queue, which is exactly the defect.
    ///     A name is not the place to guess: whoever calls this either knows who is listening or has a
    ///     bug.
    /// </remarks>
    [Fact]
    public void ASubscriberNobodySupplied_IsRefused()
    {
        var act = () => SubscriptionName.For("  ", typeof(VerificationRequested), busName: null);

        act.Should().Throw<ArgumentException>();
    }
}
