namespace Warehouse.Stock.Dtos;

/// <summary>How far an import has got: parts done, failed and in total, and each row it refused.</summary>
public sealed class ImportProgressDto
{
    public Guid ImportId { get; init; }

    /// <summary>Parts in the file.</summary>
    public int Parts { get; init; }

    /// <summary>Parts applied with every row.</summary>
    public int Done { get; init; }

    /// <summary>Parts applied with at least one row refused, or not applied at all.</summary>
    public int Failed { get; init; }

    /// <summary>Every part has been handled, one way or the other.</summary>
    public bool Finished { get; init; }

    /// <summary>The failed parts, by index — where the refused rows are.</summary>
    public List<int> FailedParts { get; init; } = [];

    public List<ImportRejectionDto> RejectedRows { get; init; } = [];
}
