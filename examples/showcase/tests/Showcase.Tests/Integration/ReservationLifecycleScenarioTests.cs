using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;
using Showcase.Billing.Actions;
using Showcase.Billing.Entities;
using Showcase.Billing.Enums;
using Showcase.Billing.Infrastructure.EventHandlers;
using Showcase.Billing.Events;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Showcase.Booking.Events;
using Xunit;

namespace Showcase.Tests.Integration;

/// <summary>
/// End-to-end scenario test covering the full reservation lifecycle:
///   Create → Confirm → (invoice created via event) → CheckIn → MarkPaid → (InvoicePaid event)
///
/// This test exercises the cross-boundary event chain (Booking → Billing) at the domain level,
/// using InMemory EF Core for state persistence.
///
/// Each phase verifies:
/// - Entity state transition (status field)
/// - Domain events raised with correct data
/// - Cross-boundary side effects (e.g., invoice created in response to ReservationConfirmed)
/// </summary>
public class ReservationLifecycleScenarioTests : IDisposable
{
    private readonly InMemoryDatabaseRoot _appRoot = new();
    private readonly InMemoryDatabaseRoot _financialRoot = new();
    private BookingDbContext _bookingDb = null!;
    private BillingDbContext _billingDb = null!;

    public ReservationLifecycleScenarioTests()
    {
        _bookingDb = CreateBookingDb();
        _billingDb = CreateBillingDb();
    }

    [Fact]
    public async Task FullReservationLifecycle_CreateToCheckIn_AllTransitionsSucceed()
    {
        // ── Phase 1: Create a reservation ──────────────────────────────────
        var guestId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var roomTypeId = Guid.NewGuid();

        var reservation = Reservation.Create(
            guestId, propertyId, roomTypeId,
            DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(10),
            numberOfGuests: 2, totalAmount: 450m, currency: "EUR",
            specialRequests: "Late check-in please");

        _bookingDb.Add(reservation);
        await _bookingDb.SaveChangesAsync();

        reservation.Status.Should().Be(ReservationStatus.Pending);
        // Anemic model: ReservationCreated is raised by [Raises<ReservationCreated>] (lifecycle) via the
        // LifecycleEventsInterceptor on a real provider — not synchronously by the factory. The end-to-end
        // raise-on-persist is covered by the Postgres-backed Showcase.IntegrationTests.

        // ── Phase 2: Confirm the reservation ──────────────────────────────
        reservation.ClearDomainEvents();
        var confirmResult = reservation.TransitionTo(ReservationStatus.Confirmed);

        confirmResult.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Confirmed);

        var confirmedEvent = reservation.DomainEvents
            .OfType<ReservationConfirmed>().Single();

        confirmedEvent.ReservationId.Should().Be(reservation.Id);
        confirmedEvent.GuestId.Should().Be(guestId);
        confirmedEvent.TotalAmount.Should().Be(450m);
        confirmedEvent.Currency.Should().Be("EUR");

        await _bookingDb.SaveChangesAsync();

        // ── Phase 3: Cross-boundary event — create invoice in Billing ──────
        // Simulates ReservationConfirmedHandler invoking CreateInvoiceAction.
        // In production, the event bus dispatches to handlers. Here we call directly.
        var invoice = CreateInvoiceFromEvent(confirmedEvent);
        _billingDb.Add(invoice);
        await _billingDb.SaveChangesAsync();

        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.TotalAmount.Should().BeGreaterThan(0m);

        // ── Phase 4: Check in the guest, assign a room ────────────────────
        reservation.ClearDomainEvents();
        reservation.SetCheckedInBy("front-desk-agent");
        var checkInResult = reservation.TransitionTo(ReservationStatus.CheckedIn);

        checkInResult.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.CheckedIn);
        reservation.DomainEvents.Should().ContainSingle(e => e is GuestCheckedIn)
            .Which.As<GuestCheckedIn>().CheckedInBy.Should().Be("front-desk-agent");

        await _bookingDb.SaveChangesAsync();

        // ── Phase 5: Mark the invoice as paid ────────────────────────────
        invoice.ClearDomainEvents();
        invoice.TransitionTo(InvoiceStatus.Paid);

        invoice.Status.Should().Be(InvoiceStatus.Paid);

        var paidEvent = invoice.DomainEvents.OfType<InvoicePaid>().Single();
        paidEvent.InvoiceId.Should().Be(invoice.Id);
        paidEvent.TotalAmount.Should().BeGreaterThan(0m);

        await _billingDb.SaveChangesAsync();

        // ── Phase 6: Verify final persisted state ─────────────────────────
        // Uses the same InMemoryDatabaseRoot so the shared in-memory state is visible.
        await using var verifyBookingDb = CreateBookingDb();
        var persisted = await verifyBookingDb
            .Set<Reservation>()
            .FindAsync(reservation.PersistenceId);

        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(ReservationStatus.CheckedIn);

        await using var verifyBillingDb = CreateBillingDb();
        var persistedInvoice = await verifyBillingDb
            .Set<Invoice>()
            .FindAsync(invoice.PersistenceId);

        persistedInvoice.Should().NotBeNull();
        persistedInvoice!.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task CancelledReservation_DoubleCancel_IsRejected()
    {
        var reservation = Reservation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(10),
            1, 200m, "EUR");

        _bookingDb.Add(reservation);
        await _bookingDb.SaveChangesAsync();

        // First cancel succeeds (anemic: the bare transition; the mutation raises ReservationCancelled with reason)
        var first = reservation.TransitionTo(ReservationStatus.Cancelled);
        first.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Cancelled);

        // State machine prevents cancelling an already-cancelled reservation
        var second = reservation.TransitionTo(ReservationStatus.Cancelled);
        second.IsFailure.Should().BeTrue("state machine rejects invalid transitions");
    }

    [Fact]
    public async Task ReservationCancelledHandler_VoidsInvoiceViaAction()
    {
        // Demonstrates the cross-boundary event handler for cancellation.
        // Uses IVoidDomainActionInvoker (no return value) — matches VoidInvoiceForReservationAction.
        var invoker = new VoidDomainActionInvokerOfVoidInvoiceForReservationActionMock();
        invoker.InvokeAsync.Returns(VoidResult<IError>.Success());

        var handler = new ReservationCancelledHandler(invoker);

        var reservationId = Guid.NewGuid();
        var cancelEvent = new ReservationCancelled(
            reservationId, Guid.NewGuid(), Guid.NewGuid(),
            "Customer request", DateTimeOffset.UtcNow);

        await handler.HandleAsync(cancelEvent);

        invoker.InvokeAsync.Received(1, Arg.Is<VoidInvoiceForReservationAction>(a => a.ReservationId == reservationId), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private BookingDbContext CreateBookingDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase("ShowcaseApp", _appRoot)
            .Options);

    private BillingDbContext CreateBillingDb() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase("ShowcaseFinancial", _financialRoot)
            .Options);

    /// <summary>
    /// Creates a draft invoice from a ReservationConfirmed event —
    /// mirrors the logic in ReservationConfirmedHandler → CreateInvoiceAction.
    /// </summary>
    private static Invoice CreateInvoiceFromEvent(ReservationConfirmed e)
    {
        var invoice = new Invoice();
        invoice.SetReservationId(e.ReservationId);
        invoice.SetGuestId(e.GuestId);
        invoice.SetSubTotal(e.TotalAmount);
        invoice.SetTaxAmount(Math.Round(e.TotalAmount * 0.10m, 2));
        invoice.SetTotalAmount(Math.Round(e.TotalAmount * 1.10m, 2));
        invoice.SetCurrency(e.Currency);
        invoice.SetInvoiceNumber($"INV-{DateTime.UtcNow:yyyyMMdd}-{invoice.Id.ToString()[..8].ToUpperInvariant()}");
        invoice.SetIssuedAt(DateTimeOffset.UtcNow);
        invoice.SetStatus(InvoiceStatus.Draft);
        return invoice;
    }

    public void Dispose()
    {
        _bookingDb.Dispose();
        _billingDb.Dispose();
    }
}
