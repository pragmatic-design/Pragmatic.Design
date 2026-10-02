using Pragmatic.MultiTenancy;

namespace Invoicing.Billing.Entities;

/// <summary>
///     The last invoice number a company used in a year.
/// </summary>
/// <remarks>
///     <para>
///         A row, not a database sequence. <c>[GeneratedValue("…{SEQ:5}")]</c> is one counter for the whole
///         table, so one company would get 1, 4, 9 while another took 2, 3, 5 — and an invoice number must
///         be consecutive <b>within the issuer</b>. The prefix is the issuer's own, which is a row too.
///     </para>
///     <para>
///         It has no operation and no endpoint: the issue action is the only thing that writes it, which is
///         why it sits beside <c>Invoice</c> and nothing addresses it.
///     </para>
/// </remarks>
[Entity]
[ConcurrencyAware]
[Unique(nameof(Year))]
public partial class InvoiceNumberSeries : IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";

    public int Year { get; private set; }

    public int LastNumber { get; private set; }

    /// <summary>Takes the next number of the year. The concurrency check is what keeps two takers apart.</summary>
    internal int Take()
    {
        SetLastNumber(LastNumber + 1);

        return LastNumber;
    }

    internal static InvoiceNumberSeries StartingIn(int year)
    {
        var series = Create();
        series.SetYear(year);

        return series;
    }
}
