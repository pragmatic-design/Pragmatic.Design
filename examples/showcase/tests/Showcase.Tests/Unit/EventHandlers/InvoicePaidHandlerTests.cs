using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Result;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Billing.Events;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.Booking.Reservations.Mutations;
using Xunit;

namespace Showcase.Tests.Unit.EventHandlers;

/// <summary>
/// Tests that InvoicePaidHandler invokes IBookingActions boundary to mark payment received.
/// Demonstrates: Cross-boundary communication via boundary interface — Billing → Booking.
/// </summary>
public class InvoicePaidHandlerTests
{
    private readonly BookingActionsMock _bookingActions =
        new BookingActionsMock();

    private readonly BookingReservationsActionsMock _reservationsActions =
        new BookingReservationsActionsMock();

    private readonly InvoicePaidHandler _sut;

    public InvoicePaidHandlerTests()
    {
        _bookingActions.Reservations.Returns(_reservationsActions);

        _reservationsActions
            .MarkPaymentReceived2.Returns(Result<Reservation, IError>.Success(new Reservation()));

        _sut = new InvoicePaidHandler(_bookingActions);
    }

    [Fact]
    public async Task HandleAsync_InvokesBookingBoundary_ToMarkPaymentReceived()
    {
        var reservationId = Guid.NewGuid();

        var @event = new InvoicePaid(
            Guid.NewGuid(), reservationId, 660m, "EUR", DateTimeOffset.UtcNow);

        await _sut.HandleAsync(@event, Pragmatic.Messaging.MessageContext.New());

        _reservationsActions.MarkPaymentReceived2.Received(1, Arg.Is<MarkPaymentReceivedMutation>(m => m.Id == reservationId), Arg.Any<CancellationToken>());
    }
}
