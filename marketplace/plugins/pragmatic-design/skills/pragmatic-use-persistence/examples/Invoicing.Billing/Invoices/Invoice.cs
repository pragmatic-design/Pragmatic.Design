using Invoicing.Billing.Errors;
using Invoicing.Registry.Dtos;
using Invoicing.Registry.Entities;
using Pragmatic.Internationalization.Types;
using Pragmatic.MultiTenancy;

namespace Invoicing.Billing.Entities;

/// <summary>
///     What a customer is asked to pay: lines, totals, and — once issued — a number and the customer as
///     they were that day.
/// </summary>
/// <remarks>
///     <para>
///         The customer is a plain <c>CustomerId</c> and <b>not</b> a navigation: <c>Customer</c> belongs
///         to Registry, and a foreign key across two boundaries is what the architecture forbids. Billing
///         reads the customer through the published contract, and copies what it needs.
///     </para>
///     <para>
///         The lines cascade: a line has no life without its invoice, and nothing addresses one on its own.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Audited]
[ConcurrencyAware]
[StateMachine<InvoiceStatus>]
[Relation.OneToMany<InvoiceLine>.WithNavigation("Lines", Inverse = "Invoice", OnDelete = DeleteBehavior.Cascade)]
// The payments do not cascade: an invoice with money against it is not something anybody deletes, and
// `Restrict` is what says so in the schema rather than in a comment. Declared on this side because the
// collection navigation is generated from the attribute that names it — `Inverse` on the child's
// [Relation.ManyToOne] names the other end for the schema, it does not generate this property.
[Relation.OneToMany<Payment>.WithNavigation("Payments", Inverse = "Invoice", OnDelete = DeleteBehavior.Restrict)]
public partial class Invoice : DomainEventSource, IEntity, ITenantEntity
{
    /// <summary>
    ///     The company this invoice belongs to. Declared by the entity and filled by the interceptor from
    ///     the tenant the request resolved; the read filter uses it, so nobody writes it by hand.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    ///     Where this invoice is in its life. Declared here and moved only through the machine, which is
    ///     what refuses every transition nobody wrote down.
    /// </summary>
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    /// <summary>Registry's customer, by id — a key across the boundary, never a navigation.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The customer's code, copied so a list of invoices reads without crossing the boundary.</summary>
    [MaxLength(20)]
    public string CustomerCode { get; private set; } = "";

    /// <summary>
    ///     The currency this example bills in. Not a column: an amount carries its own currency inside the
    ///     complex type EF maps it to, so a standalone <c>CurrencyCode</c> property would be a second
    ///     answer to the same question — and one nothing maps (PRAG0651 is right about that one).
    /// </summary>
    internal static readonly CurrencyCode Euro = CurrencyCode.FromCode("EUR");

    public Money NetTotal { get; private set; } = Money.Zero(Euro);

    public Money VatTotal { get; private set; } = Money.Zero(Euro);

    public Money GrossTotal { get; private set; } = Money.Zero(Euro);

    /// <summary>
    ///     What the customer has paid so far — the sum of the payments, kept here as well.
    /// </summary>
    /// <remarks>
    ///     A second copy of something the rows already say, on purpose: the list of invoices and the sweep
    ///     that chases the overdue ones both filter on what is still owed, and a <c>SUM</c> subquery in a
    ///     filter is not what a list read should carry. The two are kept in agreement by
    ///     <see cref="RecordPayment" /> and <see cref="RemovePayment" />, which are the only writers of
    ///     either, and <see cref="AmountPaidMatchesPayments" /> is the rule that says so out loud.
    /// </remarks>
    public Money AmountPaid { get; private set; } = Money.Zero(Euro);

    [MaxLength(500)]
    public string? Notes { get; private set; }

    /// <summary>
    ///     What the customer will quote when they pay — <c>ACME/2026/0001</c>. Null while the invoice is a
    ///     draft, and unique per company once it is not.
    /// </summary>
    [LogicKey]
    [MaxLength(30)]
    public string? Number { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    public DateOnly? DueOn { get; private set; }

    // The customer as they were the day the invoice was issued. A copy on purpose: a document already
    // sent says what it said, and moving an office next month does not rewrite it. This is the reason
    // Registry and Billing are two modules.
    [MaxLength(200)]
    public string BilledToName { get; private set; } = "";

    [MaxLength(20)]
    public string BilledToVatNumber { get; private set; } = "";

    [MaxLength(320)]
    public string BilledToEmail { get; private set; } = "";

    [MaxLength(10)]
    public string BilledToCulture { get; private set; } = "";

    public PostalAddress BilledToAddress { get; private set; } = new("", "", "", "");

    // The document made the day the invoice was issued: where it is, how long it is, and what it hashes
    // to. The hash is not decoration — it is what lets the reminder prove it attached *this* file and not
    // a fresh rendering.
    [MaxLength(400)]
    public string? PdfKey { get; private set; }

    public long? PdfByteLength { get; private set; }

    [MaxLength(64)]
    public string? PdfSha256 { get; private set; }

    /// <summary>
    ///     The day the customer was last chased about this invoice, or null if nobody has been.
    /// </summary>
    /// <remarks>
    ///     Written by the sweep and only after the message left: a send that failed leaves this alone, so
    ///     the next run tries again instead of recording a reminder nobody received.
    /// </remarks>
    public DateOnly? LastRemindedOn { get; private set; }

    /// <summary>Records that the customer was chased today.</summary>
    internal void Remind(DateOnly on) => SetLastRemindedOn(on);

    /// <summary>Why this invoice was voided, and the day it was.</summary>
    /// <remarks>
    ///     A void invoice keeps everything it had — its number, its document, its lines — and adds the
    ///     reason. Nothing is deleted and no number is released: a hole in the sequence is worse than a
    ///     void number in it, because a hole is what nobody can explain afterwards.
    /// </remarks>
    [MaxLength(300)]
    public string? VoidReason { get; private set; }

    public DateOnly? VoidedOn { get; private set; }

    /// <summary>
    ///     Voids an issued invoice: the move is the machine's, and the reason is kept.
    /// </summary>
    /// <remarks>
    ///     <c>Issued → Void</c> and from nowhere else, which is what refuses a draft and a settled invoice
    ///     without an <c>if</c> here. The caller answers for the codes: the two states that cannot be
    ///     voided mean different things to a client.
    /// </remarks>
    internal VoidResult<IError> Void(string reason, DateOnly on)
    {
        var moved = TransitionTo(InvoiceStatus.Void);
        if (moved.IsFailure)
            return moved;

        SetVoidReason(reason);
        SetVoidedOn(on);

        return VoidResult<IError>.Success();
    }

    /// <summary>
    ///     Late on <paramref name="day" />: issued, past its due date, and not settled.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A method, so the day comes from outside — the query takes it with <c>[FromClock]</c> and the
    ///         sweep passes it from the same clock. A property would have to name <c>DateTime.UtcNow</c>,
    ///         which the <em>database</em> would evaluate with its own clock, and no test could seal it.
    ///     </para>
    ///     <para>
    ///         This is the one definition of "overdue" in the application: the list filters by it through
    ///         the generated <c>InvoiceComputedFilters.IsOverdueOnSpec(day)</c>, and
    ///         <see cref="InvoiceSpecifications.ToChaseOn" /> composes that same specification with the
    ///         reminder interval. Two definitions is how a list and a reminder come to disagree.
    ///     </para>
    ///     <para>
    ///         The amounts are compared through <c>.Amount</c>: a <c>Money</c> is two columns, and only the
    ///         decimal one is a comparison SQL can make.
    ///     </para>
    /// </remarks>
    [ComputedFilter]
    public bool IsOverdueOn(DateOnly day)
        => Status == InvoiceStatus.Issued
           && DueOn != null
           && DueOn < day
           && AmountPaid.Amount < GrossTotal.Amount;

    /// <summary>
    ///     What is still owed on this invoice, as a number the database can add up.
    /// </summary>
    /// <remarks>
    ///     <c>[Projectable]</c> and not a column: it is a subtraction of two columns, and the aggregate of
    ///     <c>OutstandingByCustomerLine</c> sums <em>this body</em> — the sum happens in SQL, over rows
    ///     nothing loaded.
    /// </remarks>
    [Projectable]
    public decimal AmountOutstanding => GrossTotal.Amount - AmountPaid.Amount;

    /// <summary>Records the document rendered at issue. Written once, in the same save as the issue.</summary>
    internal void RecordDocument(string key, long byteLength, string sha256)
    {
        SetPdfKey(key);
        SetPdfByteLength(byteLength);
        SetPdfSha256(sha256);
    }

    /// <summary>
    ///     An invoice with no lines is not a document: the rule is about the whole aggregate, so it is an
    ///     invariant — checked after the mutation has applied and before the row is written, and a
    ///     violation is a 422 rather than a row nobody wanted.
    /// </summary>
    [Invariant("An invoice has at least one line")]
    public bool HasLines() => Lines.Count > 0;

    /// <summary>
    ///     <see cref="AmountPaid" /> is the sum of the payments, and never anything else.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Where it is checked. The mutations that draft and change an
    ///     invoice check it, and so does <c>RecordPaymentAction</c>, which includes <c>Payments</c> and
    ///     can therefore answer it — measured: breaking <see cref="RecordPayment" /> so it adds
    ///     the payment without moving <see cref="AmountPaid" /> answers <b>422 INVARIANT_VIOLATION</b>
    ///     with this rule's own message instead of writing the inconsistent row.
    ///     <para>
    ///         <c>VoidInvoiceAction</c> does <b>not</b> check it, on purpose: it includes the lines and not
    ///         the payments, so the rule would read an empty collection and refuse a legitimate void. What
    ///         keeps the two numbers in agreement on every path is still that <see cref="RecordPayment" />
    ///         and <see cref="RemovePayment" /> are the only writers of either, and write both.
    ///     </para>
    /// </remarks>
    [Invariant("The amount paid equals the payments recorded")]
    public bool AmountPaidMatchesPayments()
        => AmountPaid == Payments.Aggregate(Money.Zero(Euro), (total, payment) => total + payment.Amount);

    /// <summary>
    ///     Records what the customer paid: the payment joins the invoice, the amount paid goes up, and an
    ///     invoice that is now settled becomes <see cref="InvoiceStatus.Paid" />.
    /// </summary>
    /// <remarks>
    ///     More than is owed is refused here rather than by a rule on the action, because it is a fact
    ///     about the whole invoice — what it charges against what it has already received — and the action
    ///     is not the only caller there will ever be.
    /// </remarks>
    internal Result<Payment, IError> RecordPayment(
        DateOnly paidOn, Money amount, string? reference, PaymentMethod method)
    {
        // Before any arithmetic: Money refuses to add two currencies by throwing, and a 500 is not how an
        // application says "this invoice is in euros". Out of scope is not the same as undefined.
        if (amount.Currency != GrossTotal.Currency)
            return new CurrencyMismatchError { Expected = GrossTotal.Currency.Code, Offered = amount.Currency.Code };

        if (AmountPaid + amount > GrossTotal)
            return new OverpaymentError
            {
                AmountDue = (GrossTotal - AmountPaid).Amount,
                Currency = GrossTotal.Currency.Code
            };

        var payment = Payment.Record(paidOn, amount, reference, method);
        Payments.Add(payment);
        SetAmountPaid(AmountPaid + amount);

        if (AmountPaid == GrossTotal)
        {
            var moved = TransitionTo(InvoiceStatus.Paid);
            if (moved.IsFailure)
                return Result<Payment, IError>.Failure(moved.Error);
        }

        return payment;
    }

    /// <summary>
    ///     Removes a payment recorded by mistake: the amount paid goes back down, and an invoice that was
    ///     settled is owed again.
    /// </summary>
    /// <remarks>
    ///     <c>Paid → Issued</c> is a move like any other, and it is in <see cref="InvoiceStatus" /> because
    ///     of this method. Writing the status by hand here would be a transition nobody could review.
    /// </remarks>
    internal VoidResult<IError> RemovePayment(Payment payment)
    {
        Payments.Remove(payment);
        SetAmountPaid(AmountPaid - payment.Amount);

        return Status == InvoiceStatus.Paid
            ? TransitionTo(InvoiceStatus.Issued)
            : VoidResult<IError>.Success();
    }

    /// <summary>
    ///     Recomputes every line and the three totals.
    /// </summary>
    /// <remarks>
    ///     Rounded <b>per line</b> and then added, which is what an invoice must do: a <c>SUM</c> of
    ///     unrounded products drifts by a cent as soon as two lines end in half a cent, and the total on
    ///     the paper would not be the sum of the lines printed above it.
    /// </remarks>
    /// <summary>
    ///     Becomes a document: the number it will be quoted by, the day it was issued, when it is due, and
    ///     the customer frozen as they are today.
    /// </summary>
    /// <remarks>
    ///     The move is the machine's — <c>Draft → Issued</c> and from nowhere else — so an invoice already
    ///     issued is refused here and not by an <c>if</c> somebody remembered to write.
    /// </remarks>
    internal Result<bool, IError> Issue(
        string number, DateOnly issuedOn, int paymentTermsDays, CustomerBillingDetailsDto customer)
    {
        var moved = TransitionTo(InvoiceStatus.Issued);
        if (moved.IsFailure)
            return Result<bool, IError>.Failure(moved.Error);

        SetNumber(number);
        SetIssuedOn(issuedOn);
        SetDueOn(issuedOn.AddDays(paymentTermsDays));

        SetBilledToName(customer.Name);
        SetBilledToVatNumber(customer.VatNumber);
        SetBilledToEmail(customer.Email);
        SetBilledToCulture(customer.PreferredCulture);
        SetBilledToAddress(new PostalAddress(
            customer.Address.Street, customer.Address.PostCode, customer.Address.City, customer.Address.Country));

        return true;
    }

    internal void Recalculate()
    {
        var net = Money.Zero(Euro);
        var vat = Money.Zero(Euro);

        foreach (var line in Lines)
        {
            line.Recalculate(Euro);
            net += line.LineNet;
            vat += line.LineVat;
        }

        SetNetTotal(net);
        SetVatTotal(vat);
        SetGrossTotal(net + vat);
    }
}
