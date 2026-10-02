namespace Invoicing.Billing.Entities;

/// <summary>The one rule a series is read by: the company's series for a year.</summary>
/// <remarks>
///     The company is not in the rule: the tenant filter is, and it is what keeps two companies' series
///     apart without anybody writing the condition.
/// </remarks>
public static partial class InvoiceNumberSeriesSpecifications
{
    public static Specification<InvoiceNumberSeries> ForYear(int year)
        => Spec<InvoiceNumberSeries>.Where(series => series.Year == year);
}
