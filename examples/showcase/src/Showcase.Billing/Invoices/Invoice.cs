using Showcase.Billing.Events;
using Showcase.Booking.Entities;

namespace Showcase.Billing.Entities;

/// <summary>
/// An invoice generated for a reservation.
/// Demonstrates [Relation.*] attributes: 2 OneToMany (same boundary) + 2 ManyToOne (cross-boundary).
/// Read-only: managed by domain actions.
/// </summary>
[Entity]
[Auditable]
[SoftDelete]
[HasAccessScopes]
[ConcurrencyAware]
[StateMachine<InvoiceStatus>]
[Relation.OneToMany<LineItem>]
[Relation.OneToMany<Fee>]
[Relation.OneToMany<Payment>]
[Relation.ManyToOne<Reservation>]
[Relation.ManyToOne<Guest>]
public partial class Invoice : DomainEventSource, IEntity
{
    // FK kept in source for cross-SG visibility (manual DTOs with [MapFrom] reference them)
    public Guid ReservationId { get; private set; }
    public Guid GuestId { get; private set; }

    /// <summary>
    /// Auto-generated invoice number, e.g. INV-202601-00001. The {SEQ:5} token is backed by a real
    /// database sequence — [GeneratedValue] wires the generated default-value generator itself,
    /// so no hand-written IDefaultValueGenerator/[ComputedDefault] is needed.
    /// </summary>
    [LogicKey]
    [GeneratedValue("INV-{YYYY}{MM}-{SEQ:5}")]
    public string InvoiceNumber { get; private set; } = "";

    public decimal SubTotal { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Default tax rate (10%). Centralized to avoid duplication across handlers/actions.</summary>
    public const decimal TaxRate = 0.10m;

    /// <summary>Calculates tax from a subtotal using the standard rate.</summary>
    public static decimal CalculateTax(decimal subTotal) => Math.Round(subTotal * TaxRate, 2);

    public string Currency { get; private set; } = "EUR";

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? DueDate { get; private set; }

    /// <summary>Whether the invoice is past its due date and unpaid. SQL-translatable via Invoice.Expr.IsOverdue.</summary>
    [Projectable]
    [ComputedFilter]
    public bool IsOverdue => DueDate != null && DueDate < DateTimeOffset.UtcNow && Status != InvoiceStatus.Paid;

    /// <summary>The user who authorized the payment (from JWT "sub" claim).</summary>
    public Guid? PaidByUserId { get; private set; }

    /// <summary>
    /// Provider-assigned refund transaction reference. The refund operation sets this before transitioning to
    /// Refunded, so [RaisesEvent&lt;InvoiceRefunded&gt;] on that state can carry it. Anemic model: no behavior
    /// method — the Paid-&gt;Refunded guard is the state machine ([TransitionFrom(Paid)] on Refunded).
    /// </summary>
    public string? RefundTransactionId { get; private set; }
}
