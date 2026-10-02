namespace Invoicing.Billing.Invoices.Queries;

/// <summary>
///     One line of "who owes us what": a customer, how many of their invoices are open, how much is still
///     owed, and the due date of the oldest one.
/// </summary>
/// <remarks>
///     <para>
///         The grouping is declared here and computed by the database: one <c>GROUP BY</c> over the
///         invoices, nothing loaded to be added up in C#. What is summed is
///         <see cref="Invoice.AmountOutstanding" />, a <c>[Projectable]</c> — the aggregate takes its body,
///         so the subtraction happens in SQL too.
///     </para>
///     <para>
///         ⚠️ The customer's <b>name</b> is not here, and that is not an omission. The name on an invoice
///         is the copy frozen the day it was issued, so it is a property of the invoice and not of the
///         customer: two invoices of the same customer can legitimately carry two names, and an aggregate
///         that picked one would be choosing. The identity is <see cref="CustomerCode" /> — and
///         <c>GET api/customers/{id}</c> is where the name they go by today lives.
///     </para>
/// </remarks>
[QueryView<Invoice>]
[GroupBy<Invoice>(Properties = "CustomerId,CustomerCode")]
public partial class OutstandingByCustomerLine
{
    public Guid CustomerId { get; init; }

    public string CustomerCode { get; init; } = "";

    /// <summary>How many open invoices make this line up.</summary>
    [Count<Invoice>]
    public int Invoices { get; init; }

    /// <summary>The sum still owed, in the currency the company bills in.</summary>
    [Sum<Invoice>(Expression = nameof(Invoice.AmountOutstanding))]
    public decimal Outstanding { get; init; }

    /// <summary>The due date of the oldest open invoice — how far back the debt goes.</summary>
    [Min<Invoice>(Expression = nameof(Invoice.DueOn))]
    public DateOnly? OldestDueOn { get; init; }
}
