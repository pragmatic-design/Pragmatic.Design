namespace Pragmatic.Documents.Benchmarks.Workload;

/// <summary>
///     The report a template is given with its type known: the generator writes accessors for it and, through
///     <see cref="Lines" />, for <see cref="InvoiceLine" />.
/// </summary>
public sealed class InvoiceReport
{
    public required string Title { get; init; }
    public required List<InvoiceLine> Lines { get; init; }
}
