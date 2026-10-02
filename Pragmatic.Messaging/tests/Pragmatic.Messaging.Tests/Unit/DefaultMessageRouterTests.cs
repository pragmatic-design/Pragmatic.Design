using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Routing;

// Test message types with realistic namespaces (block-scoped to avoid conflict)
namespace Showcase.Booking.Events { public record ReservationCreated; }
namespace Showcase.Billing.Events { public record InvoicePaid; }
namespace Showcase.Billing.EventHandlers { public class InvoicePaidHandler { } }

namespace Pragmatic.Messaging.Tests.Unit
{
    public class DefaultMessageRouterTests
    {
        private readonly DefaultMessageRouter _router = new();

        [Fact]
        public void GetTopic_ShouldReturnBoundaryBasedTopic()
        {
            var topic = _router.GetTopic(typeof(Showcase.Booking.Events.ReservationCreated));
            topic.Should().Be("booking.events");
        }

        [Fact]
        public void GetTopic_DifferentBoundary_ShouldReturnCorrectTopic()
        {
            var topic = _router.GetTopic(typeof(Showcase.Billing.Events.InvoicePaid));
            topic.Should().Be("billing.events");
        }

        [Fact]
        public void GetTopic_Generic_ShouldReturnSameAsDirect()
        {
            var topicGeneric = _router.GetTopic<Showcase.Booking.Events.ReservationCreated>();
            var topicDirect = _router.GetTopic(typeof(Showcase.Booking.Events.ReservationCreated));
            topicGeneric.Should().Be(topicDirect);
        }

    }
}
