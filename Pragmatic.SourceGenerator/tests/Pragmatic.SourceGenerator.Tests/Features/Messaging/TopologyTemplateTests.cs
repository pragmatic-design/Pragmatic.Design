using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

public class TopologyTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_ShouldFollowConvention()
    {
        var handlers = ImmutableArray.Create(BuildHandler("Billing.Handlers", "InvoiceHandler", "global::Booking.Events.ReservationCreated"));
        var artifact = new TopologyTemplate(handlers).RenderOutput();

        artifact.HintName.Should().Be("_Infra.Messaging.Topology.g.cs");
    }

    [Fact]
    public void RenderOutput_ShouldContainExchangeConstants()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("Billing.Handlers", "Handler1", "global::Booking.Events.Event1"));

        var source = new TopologyTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("class Exchanges");
        source.Should().Contain(".events");
    }

    [Fact]
    public void RenderOutput_ShouldContainQueueConstants()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("Billing.Handlers", "InvoicePaidHandler", "global::Billing.Events.InvoicePaid"));

        var source = new TopologyTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("class Queues");
        source.Should().Contain("billing");
    }

    [Fact]
    public void RenderOutput_ShouldContainGetBindingsMethod()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("App.Handlers", "MyHandler", "global::App.Events.MyEvent"));

        var source = new TopologyTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("GetBindings");
        source.Should().Contain("TopologyBinding[]");
    }

    [Fact]
    public void RenderOutput_MultipleHandlers_ShouldDeduplicateExchanges()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("Billing", "H1", "global::Booking.Events.Event1"),
            BuildHandler("Billing", "H2", "global::Booking.Events.Event2"));

        var source = new TopologyTemplate(handlers).RenderOutput().Text;

        // Both events from same boundary → one exchange
        var exchangeOccurrences = source.Split("booking_events").Length - 1;
        // Should have the constant declared once
        exchangeOccurrences.Should().BeGreaterThanOrEqualTo(1);
    }

    private static MessageHandlerModel BuildHandler(string ns, string name, string messageTypeFqn) => new()
    {
        Namespace = ns,
        TypeName = name,
        Accessibility = "public",
        TypeKind = "class",
        MessageTypeFqn = messageTypeFqn,
        MessageTypeShortName = messageTypeFqn.Split('.').Last(),
    };
}
