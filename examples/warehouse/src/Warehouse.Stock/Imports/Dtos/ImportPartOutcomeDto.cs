namespace Warehouse.Stock.Dtos;

/// <summary>What applying one part of an import did.</summary>
public sealed class ImportPartOutcomeDto
{
    /// <summary>
    ///     The part had already been applied: this delivery wrote nothing, and the counts are those of the
    ///     first delivery.
    /// </summary>
    public bool AlreadyApplied { get; init; }

    public int Applied { get; init; }

    public int Rejected { get; init; }
}
