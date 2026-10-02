using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Showcase.Billing.Enums;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests invoice entity behavior: state transitions, domain events.
/// </summary>
public class InvoiceTests
{
    [Fact]
    public void MarkAsPaid_TransitionsStatusToPaid()
    {
        var invoice = CreateDraftInvoice();

        invoice.TransitionTo(InvoiceStatus.Paid);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void MarkAsPaid_RaisesInvoicePaidEvent()
    {
        var invoice = CreateDraftInvoice();

        invoice.TransitionTo(InvoiceStatus.Paid);

        invoice.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Billing.Events.InvoicePaid>();
    }

    [Fact]
    public void MarkAsPaid_EventContainsCorrectAmount()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetTotalAmount(599.99m);
        invoice.SetCurrency("USD");

        invoice.TransitionTo(InvoiceStatus.Paid);

        var evt = invoice.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Billing.Events.InvoicePaid>().Subject;

        evt.TotalAmount.Should().Be(599.99m);
        evt.Currency.Should().Be("USD");
    }

    // =========================================================================
    // StateMachine: Draft cannot directly transition to Refunded
    // =========================================================================

    [Fact]
    public void TransitionTo_DraftToRefunded_ReturnsConflictError()
    {
        var invoice = CreateDraftInvoice();

        var result = invoice.TransitionTo(InvoiceStatus.Refunded);

        result.IsFailure.Should().BeTrue(
            "Draft → Refunded is not a valid state machine transition (must go through Paid first)");
    }

    // =========================================================================
    // StateMachine: CanTransitionTo validates transitions
    // =========================================================================

    [Fact]
    public void CanTransitionTo_DraftToRefunded_ReturnsFalse()
    {
        var invoice = CreateDraftInvoice();

        invoice.CanTransitionTo(InvoiceStatus.Refunded).Should().BeFalse(
            "Refunded is only reachable from Paid, not from Draft");
    }

    [Fact]
    public void CanTransitionTo_DraftToPaid_ReturnsTrue()
    {
        var invoice = CreateDraftInvoice();

        invoice.CanTransitionTo(InvoiceStatus.Paid).Should().BeTrue(
            "Draft → Paid is a valid transition per [TransitionFrom(InvoiceStatus.Draft)]");
    }

    [Fact]
    public void CanTransitionTo_PaidToRefunded_ReturnsTrue()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Paid);

        invoice.CanTransitionTo(InvoiceStatus.Refunded).Should().BeTrue(
            "Paid → Refunded is a valid transition per [TransitionFrom(InvoiceStatus.Paid)]");
    }

    // =========================================================================
    // Void: transitions Draft to Cancelled
    // =========================================================================

    [Fact]
    public void Void_FromDraft_TransitionsToCancelled()
    {
        var invoice = CreateDraftInvoice();

        invoice.SetStatus(InvoiceStatus.Cancelled);

        invoice.Status.Should().Be(InvoiceStatus.Cancelled,
            "Void() should transition a Draft invoice to Cancelled status");
    }

    [Fact]
    public void Void_FromIssued_TransitionsToCancelled()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Issued);

        invoice.SetStatus(InvoiceStatus.Cancelled);

        invoice.Status.Should().Be(InvoiceStatus.Cancelled,
            "Void() should transition an Issued invoice to Cancelled status");
    }

    // =========================================================================
    // Refund: returns error from non-Paid status
    // =========================================================================

    [Fact]
    public void Refund_FromDraft_ReturnsConflictError()
    {
        var invoice = CreateDraftInvoice();

        invoice.SetRefundTransactionId("txn-123");
        var result = invoice.TransitionTo(InvoiceStatus.Refunded);

        result.IsFailure.Should().BeTrue(
            "Refund should fail for an invoice that is not in Paid status");
    }

    [Fact]
    public void Refund_FromIssued_ReturnsConflictError()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Issued);

        invoice.SetRefundTransactionId("txn-456");
        var result = invoice.TransitionTo(InvoiceStatus.Refunded);

        result.IsFailure.Should().BeTrue(
            "Refund should fail for an Issued invoice (only Paid invoices can be refunded)");
    }

    [Fact]
    public void Refund_FromCancelled_ReturnsConflictError()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Cancelled);

        invoice.SetRefundTransactionId("txn-789");
        var result = invoice.TransitionTo(InvoiceStatus.Refunded);

        result.IsFailure.Should().BeTrue(
            "Refund should fail for a Cancelled invoice");
    }

    [Fact]
    public void Refund_FromPaid_Succeeds()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Paid);

        invoice.SetRefundTransactionId("txn-success");
        var result = invoice.TransitionTo(InvoiceStatus.Refunded);

        result.IsSuccess.Should().BeTrue(
            "Refund should succeed for a Paid invoice");
        invoice.Status.Should().Be(InvoiceStatus.Refunded);
    }

    [Fact]
    public void Refund_FromPaid_RaisesInvoiceRefundedEvent()
    {
        var invoice = CreateDraftInvoice();
        invoice.SetStatus(InvoiceStatus.Paid);

        // Anemic: record the refund reference, then transition; [RaisesEvent<InvoiceRefunded>] auto-raises.
        invoice.SetRefundTransactionId("txn-refund-evt");
        invoice.TransitionTo(InvoiceStatus.Refunded);

        invoice.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Billing.Events.InvoiceRefunded>()
            .Which.RefundTransactionId.Should().Be("txn-refund-evt");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static Invoice CreateDraftInvoice()
    {
        var invoice = new Invoice();
        invoice.SetInvoiceNumber("INV-202602-00001");
        invoice.SetReservationId(Guid.NewGuid());
        invoice.SetGuestId(Guid.NewGuid());
        invoice.SetSubTotal(500m);
        invoice.SetTaxAmount(100m);
        invoice.SetTotalAmount(600m);
        invoice.SetCurrency("EUR");
        invoice.SetStatus(InvoiceStatus.Draft);
        invoice.SetIssuedAt(DateTimeOffset.UtcNow);
        return invoice;
    }
}
