namespace Pragmatic.Documents.Benchmarks.Workload;

/// <summary><see cref="InvoiceReport" />'s twin, only ever added as <see cref="object" />.</summary>
public sealed class ReflectedInvoiceReport
{
    public required string Title { get; init; }
    public required List<ReflectedInvoiceLine> Lines { get; init; }
}
