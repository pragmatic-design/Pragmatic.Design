namespace Warehouse.Stock.Entities;

/// <summary>
///     A row of a supplier's file that was not applied, and why — so whoever sent the file can correct
///     that line and nothing else.
/// </summary>
/// <remarks>
///     Written only with its part (<see cref="ImportedPart" />), in the same transaction: it has no
///     operation of its own.
/// </remarks>
[Entity]
public partial class ImportRejection : IEntity
{
    public Guid ImportId { get; private set; }

    public int PartIndex { get; private set; }

    /// <summary>The row's line in the file, the header being line 1.</summary>
    public int Line { get; private set; }

    [MaxLength(40)]
    public string Sku { get; private set; } = "";

    [Required]
    [MaxLength(200)]
    public string Reason { get; private set; } = "";

    internal static ImportRejection Of(Guid importId, int partIndex, int line, string sku, string reason)
    {
        var rejection = Create();
        rejection.ImportId = importId;
        rejection.PartIndex = partIndex;
        rejection.Line = line;
        rejection.Sku = sku.Length > 40 ? sku[..40] : sku;
        rejection.Reason = reason;
        return rejection;
    }
}
