namespace Warehouse.Stock.Entities;

/// <summary>
///     A part of an import, applied: how many of its rows became receipts, and how many were refused.
/// </summary>
/// <remarks>
///     One per part, by the unique index on (<see cref="ImportId" />, <see cref="PartIndex" />): a part
///     delivered twice finds it and applies nothing, and the index holds when two deliveries race past the
///     lookup — one commits, the other is retried and finds it.
/// </remarks>
[Entity]
[Unique(nameof(ImportId), nameof(PartIndex))]
public partial class ImportedPart : IEntity
{
    /// <summary>The import, by its batch id.</summary>
    public Guid ImportId { get; private set; }

    public int PartIndex { get; private set; }

    /// <summary>Rows that became a receipt.</summary>
    public int Applied { get; private set; }

    /// <summary>Rows refused, each with an <see cref="ImportRejection" /> that says why.</summary>
    public int Rejected { get; private set; }

    internal static ImportedPart Of(Guid importId, int partIndex, int applied, int rejected)
    {
        var part = Create();
        part.ImportId = importId;
        part.PartIndex = partIndex;
        part.Applied = applied;
        part.Rejected = rejected;
        return part;
    }
}
