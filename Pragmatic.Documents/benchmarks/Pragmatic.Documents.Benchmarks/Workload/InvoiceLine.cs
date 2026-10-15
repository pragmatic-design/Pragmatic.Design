namespace Pragmatic.Documents.Benchmarks.Workload;

/// <summary>One row of the report: ten fields, the shape every row of the workload has.</summary>
public sealed class InvoiceLine
{
    public required int Number { get; init; }
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Discount { get; init; }
    public required decimal VatRate { get; init; }
    public required decimal Net { get; init; }
    public required DateTime ShippedOn { get; init; }
}
