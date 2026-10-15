namespace Pragmatic.Documents.Benchmarks.Workload;

/// <summary>
///     <see cref="InvoiceLine" />'s twin, reached only through a report added as <see cref="object" />.
/// </summary>
/// <remarks>
///     A separate type, not the same one added untyped: the generated accessors are looked up by runtime
///     type, so an <see cref="InvoiceLine" /> would be read through them however it was added, and the
///     reflection row would measure the generated path twice.
/// </remarks>
public sealed class ReflectedInvoiceLine
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
