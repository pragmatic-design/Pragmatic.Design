using Pragmatic.Documents.Csv;

namespace Showcase.Billing.Dtos;

/// <summary>
/// One line of the invoice export an accounts department opens in a spreadsheet.
/// </summary>
/// <remarks>
/// <para>
/// The columns are <b>declared</b>: <c>[CsvSerializable]</c> generates a nested <c>Csv</c> class with
/// <c>Headers</c>, <c>Write</c> and <c>Read</c> — typed, zero reflection, AOT-safe — and
/// <c>[CsvColumn]</c> says what each column is called, where it sits and how it is formatted. The
/// XLSX export beside this one still writes its header row as a list of strings, and the two are
/// worth reading together: there, renaming a column is an edit in two places that nothing checks.
/// </para>
/// <para>
/// ⚠️ <c>Id</c> carries <c>Ignore = true</c> rather than being left off the type. The row is built
/// from the same DTO the API answers with, so the property exists; what this declares is that the
/// internal key is not part of the file somebody opens — a decision, made where a reader of the
/// export will look for it.
/// </para>
/// </remarks>
[CsvSerializable]
public sealed partial class InvoiceCsvRow
{
    /// <summary>The number the invoice is known by outside this system.</summary>
    [CsvColumn("Invoice number", Order = 0)]
    public string InvoiceNumber { get; init; } = "";

    /// <summary>The day it was issued — a date, not an instant, because that is what a ledger uses.</summary>
    [CsvColumn("Issued", Order = 1, Format = "yyyy-MM-dd")]
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>What is owed, at two decimals whatever the culture of the machine writing it.</summary>
    [CsvColumn("Total", Order = 2, Format = "0.00")]
    public decimal TotalAmount { get; init; }

    [CsvColumn("Currency", Order = 3)]
    public string Currency { get; init; } = "";

    [CsvColumn("Status", Order = 4)]
    public string Status { get; init; } = "";

    /// <summary>The internal key. Not in the file: see the remark on the type.</summary>
    [CsvColumn(Ignore = true)]
    public Guid Id { get; init; }
}
