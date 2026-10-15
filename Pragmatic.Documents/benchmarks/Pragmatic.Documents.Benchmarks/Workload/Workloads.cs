namespace Pragmatic.Documents.Benchmarks.Workload;

/// <summary>The document every row of the suite produces: a report with a table of 1,000 rows of ten fields.</summary>
public static class Workloads
{
    public const int Rows = 1_000;

    public const int Fields = 10;

    public static readonly string[] Headers =
        ["#", "SKU", "Description", "Category", "Qty", "Unit price", "Discount", "VAT", "Net", "Shipped"];

    /// <summary>A table template whose cells read every field of every row, unformatted, so a row is data access.</summary>
    public const string ReportTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <document title="{{ report.title }}">
          <page>
            <heading level="2">{{ report.title }}</heading>
            <table data-source="report.lines" repeat-header="true">
              <column width="10">#</column>
              <column width="18">SKU</column>
              <column width="30">Description</column>
              <column width="18">Category</column>
              <column width="10" align="right">Qty</column>
              <column width="16" align="right">Unit price</column>
              <column width="14" align="right">Discount</column>
              <column width="10" align="right">VAT</column>
              <column width="16" align="right">Net</column>
              <column width="22">Shipped</column>
              <row-template>
                <cell>{{ item.number }}</cell>
                <cell>{{ item.sku }}</cell>
                <cell>{{ item.description }}</cell>
                <cell>{{ item.category }}</cell>
                <cell>{{ item.quantity }}</cell>
                <cell>{{ item.unitPrice }}</cell>
                <cell>{{ item.discount }}</cell>
                <cell>{{ item.vatRate }}</cell>
                <cell>{{ item.net }}</cell>
                <cell>{{ item.shippedOn }}</cell>
              </row-template>
            </table>
          </page>
        </document>
        """;

    private const string Title = "Shipped lines, Q3";

    private static readonly string[] Categories = ["Hardware", "Software", "Services", "Training"];

    public static InvoiceReport Typed() => new() { Title = Title, Lines = [.. Enumerable.Range(1, Rows).Select(Line)] };

    public static ReflectedInvoiceReport Reflected() => new()
    {
        Title = Title,
        Lines = [.. Enumerable.Range(1, Rows).Select(Line).Select(l => new ReflectedInvoiceLine
        {
            Number = l.Number, Sku = l.Sku, Description = l.Description, Category = l.Category,
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount, VatRate = l.VatRate,
            Net = l.Net, ShippedOn = l.ShippedOn,
        })],
    };

    private static InvoiceLine Line(int i)
    {
        var quantity = 1 + i % 12;
        var unitPrice = 9.5m + i % 37;
        var discount = i % 5 == 0 ? 0.1m : 0m;

        return new InvoiceLine
        {
            Number = i,
            Sku = $"SKU-{i:D5}",
            Description = $"Line item {i} of the quarterly shipment",
            Category = Categories[i % Categories.Length],
            Quantity = quantity,
            UnitPrice = unitPrice,
            Discount = discount,
            VatRate = 0.22m,
            Net = quantity * unitPrice * (1 - discount),
            ShippedOn = new DateTime(2026, 7, 1).AddDays(i % 92),
        };
    }
}
