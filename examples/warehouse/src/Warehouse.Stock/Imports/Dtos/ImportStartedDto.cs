namespace Warehouse.Stock.Dtos;

/// <summary>An import dispatched: the id its progress is read by, and how it was cut.</summary>
public sealed class ImportStartedDto
{
    /// <summary>The batch id, and the import's: <c>GET api/imports/{id}</c>.</summary>
    public Guid ImportId { get; init; }

    public int Rows { get; init; }

    public int Parts { get; init; }
}
