using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Showcase.Billing.Events;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Booking.Reservations.Mutations;
using Xunit;

namespace Showcase.Tests.Unit.EventHandlers;

/// <summary>
///     <c>[CircuitBreaker]</c>: once Booking has failed enough, the handler stops being
///     called at all.
/// </summary>
/// <remarks>
///     <para>
///         <c>InvoicePaidHandler</c> already declared <c>[Retry]</c>, and retry alone against a
///         dependency that is <em>down</em> costs four attempts and three and a half seconds for
///         every message in the queue. The breaker is what turns that into one failure and then
///         nothing.
///     </para>
///     <para>
///         ⚠️ <b>Short-circuited, not merely failing.</b> A breaker that never opens and a
///         dependency that is simply down look identical from the outside: both throw. What tells
///         them apart is whether the handler was <em>entered</em> — so the assertion is on the
///         dependency's call count, which stops rising, and on which exception comes back.
///     </para>
///     <para>
///         The whole sequence is one test because the breaker's state is static per handler type:
///         split across two methods it would depend on their order, which is the shape of
///         <c>a-maximum-is-the-statistic-a-shared-machine-ruins</c>.
///     </para>
/// </remarks>
public class WhenBookingIsDownTheHandlerStopsTryingTests
{
    private static readonly InvoicePaid Paid =
        new(Guid.NewGuid(), Guid.NewGuid(), 120m, "EUR", DateTimeOffset.UtcNow);

    [Fact]
    public async Task OnceTheCircuitIsOpen_TheHandlerIsNotEnteredAgain()
    {
        var booking = new BookingActionsMock();
        var reservations = new BookingReservationsActionsMock();
        booking.Reservations.Returns(reservations);
        reservations.MarkPaymentReceived2.Throws(new HttpRequestException("Booking is down"));

        var pipeline = new InvoicePaidHandler.Pipeline(
            new InvoicePaidHandler(booking),
            NullLogger<InvoicePaidHandler.Pipeline>.Instance,
            []);

        // One message, exhausting its retries: four attempts, four recorded failures, and the
        // threshold of three reached on the way.
        var downstream = await Assert.ThrowsAsync<HttpRequestException>(
            () => pipeline.ExecuteAsync(Paid, MessageContext.New()));
        downstream.Message.Should().Be("Booking is down");

        reservations.MarkPaymentReceived2.Received(
            4, Arg.Any<MarkPaymentReceivedMutation>(), Arg.Any<CancellationToken>());

        // The next message does not reach the handler at all.
        var open = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync(Paid, MessageContext.New()));

        open.Message.Should().Contain("Circuit breaker open",
            "the second message is refused by the gate, not by Booking — an HttpRequestException "
            + "here would mean the call went out again");

        reservations.MarkPaymentReceived2.Received(
            4, Arg.Any<MarkPaymentReceivedMutation>(), Arg.Any<CancellationToken>());
    }
}
