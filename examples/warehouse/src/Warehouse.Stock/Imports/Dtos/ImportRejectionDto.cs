namespace Warehouse.Stock.Dtos;

/// <summary>A row not applied: its line in the file, its SKU as written, and why.</summary>
[MapFrom<ImportRejection>]
public partial class ImportRejectionDto
{
    public int Line { get; init; }

    public string Sku { get; init; } = "";

    public string Reason { get; init; } = "";

    public int PartIndex { get; init; }
}
