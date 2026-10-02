using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Result;
using Showcase.Billing;
using Showcase.Billing.Entities;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Booking.Events;
using Xunit;

namespace Showcase.Tests.Unit.EventHandlers;

/// <summary>
/// Tests that ReservationConfirmedHandler delegates invoice creation via IBillingActions boundary.
/// Demonstrates: Event-driven cross-boundary integration (Booking → Billing) using boundary interface.
/// </summary>
public class ReservationConfirmedHandlerTests
{
    private readonly BillingInternalActionsMock _billingActions = new BillingInternalActionsMock();
    private readonly ReservationConfirmedHandler _sut;

    public ReservationConfirmedHandlerTests()
    {
        _sut = new ReservationConfirmedHandler(_billingActions);
    }

    [Fact]
    public async Task HandleAsync_InvokesBillingBoundaryWithUnwrappedParameters()
    {
        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        const decimal totalAmount = 600m;
        const string currency = "EUR";
        var occurredAt = DateTimeOffset.UtcNow;

        var @event = new ReservationConfirmed(
            reservationId, guestId, Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(8),
            totalAmount, currency, occurredAt);

        await _sut.HandleAsync(@event, Pragmatic.Messaging.MessageContext.New());

        var expectedTax = Invoice.CalculateTax(totalAmount);

        _billingActions.CreateDraftInvoice9.Received(1, a =>
            Equals(a[0], reservationId) && Equals(a[1], guestId) && Equals(a[2], totalAmount)
            && Equals(a[3], expectedTax) && Equals(a[4], totalAmount + expectedTax)
            && Equals(a[5], currency) && Equals(a[6], occurredAt));
    }
}
