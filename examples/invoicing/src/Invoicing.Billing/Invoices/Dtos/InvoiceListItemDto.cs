using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Dtos;

/// <summary>
///     One line of the invoice list: what an accountant scans a page of.
/// </summary>
/// <remarks>
///     <para>
///         Projected by the database — <c>[GenerateProjection]</c> — so a page is one statement whatever
///         the number of rows, and no invoice is loaded to be summarised. The lines are not here: a list
///         that carried them would read the children of every row on the page, which is the N+1 nobody
///         notices until the page is slow.
///     </para>
///     <para>
///         The customer is the <b>frozen</b> copy, as everywhere in Billing: the document says what it said
///         the day it was issued, and a list built from the register would show today's name against last
///         year's invoice.
///     </para>
/// </remarks>
[MapFrom<Invoice>]
[GenerateProjection]
public partial class InvoiceListItemDto
{
    public Guid Id { get; init; }

    public string? Number { get; init; }

    public InvoiceStatus Status { get; init; }

    public Guid CustomerId { get; init; }

    public string CustomerCode { get; init; } = "";

    public string BilledToName { get; init; } = "";

    public DateOnly? IssuedOn { get; init; }

    public DateOnly? DueOn { get; init; }

    public Money GrossTotal { get; init; }

    public Money AmountPaid { get; init; }

    /// <summary>
    ///     What is still owed on this invoice.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Computed here, and <b>not</b> a flag saying whether the invoice is late. "Late" is a question
    ///     about a day, and the day cannot reach a projection: the clock the application reads lives in its
    ///     own process, while a projection is evaluated by the database. The list <em>filters</em> by it —
    ///     <c>?overdue=true</c>, through <c>Invoice.IsOverdueOn(day)</c> with the day from
    ///     <c>[FromClock]</c> — and a caller that wants to mark the rows has <see cref="DueOn" /> and this.
    /// </remarks>
    [MapIgnore]
    public Money AmountDue => GrossTotal - AmountPaid;
}
