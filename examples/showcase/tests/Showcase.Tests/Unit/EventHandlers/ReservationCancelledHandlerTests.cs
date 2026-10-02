using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;
using Showcase.Billing.Actions;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Booking.Events;
using Xunit;

namespace Showcase.Tests.Unit.EventHandlers;

/// <summary>
/// Tests that ReservationCancelledHandler delegates invoice voiding to VoidInvoiceForReservationAction.
/// Demonstrates: Event-driven cross-boundary integration (Booking → Billing) via void action pipeline.
/// </summary>
public class ReservationCancelledHandlerTests
{
    private readonly VoidDomainActionInvokerOfVoidInvoiceForReservationActionMock _voidInvoice =
        new VoidDomainActionInvokerOfVoidInvoiceForReservationActionMock();

    private readonly ReservationCancelledHandler _sut;

    public ReservationCancelledHandlerTests()
    {
        _voidInvoice.InvokeAsync.Returns(VoidResult<IError>.Success());

        _sut = new ReservationCancelledHandler(_voidInvoice);
    }

    [Fact]
    public async Task HandleAsync_InvokesVoidInvoiceWithReservationId()
    {
        var reservationId = Guid.NewGuid();
        var @event = new ReservationCancelled(
            reservationId,
            GuestId: Guid.NewGuid(),
            PropertyId: Guid.NewGuid(),
            Reason: "Guest request",
            OccurredAt: DateTimeOffset.UtcNow);

        await _sut.HandleAsync(@event);

        _voidInvoice.InvokeAsync.Received(1, Arg.Is<VoidInvoiceForReservationAction>(a =>
                a.ReservationId == reservationId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var @event = new ReservationCancelled(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Cancelled by system", DateTimeOffset.UtcNow);

        await _sut.HandleAsync(@event, cts.Token);

        _voidInvoice.InvokeAsync.Received(1, Arg.Any<VoidInvoiceForReservationAction>(), cts.Token);
    }
}
