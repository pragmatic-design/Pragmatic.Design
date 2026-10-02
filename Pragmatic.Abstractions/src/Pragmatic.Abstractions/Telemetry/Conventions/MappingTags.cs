namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for projection and mapping.
/// </summary>
public static class MappingTags
{
    /// <summary>The entity type being projected from.</summary>
    public const string SourceType = "pragmatic.mapping.source_type";

    /// <summary>The DTO type being projected to.</summary>
    public const string DtoType = "pragmatic.mapping.dto_type";

    /// <summary>Whether a projection was found for the pair.</summary>
    public const string Found = "pragmatic.mapping.found";

    /// <summary>Number of items in the projected result.</summary>
    public const string ResultCount = "pragmatic.mapping.result_count";

    /// <summary>Total matching items, before paging.</summary>
    public const string TotalCount = "pragmatic.mapping.total_count";

    /// <summary>The requested page number.</summary>
    public const string PageNumber = "pragmatic.mapping.page_number";

    /// <summary>The requested page size.</summary>
    public const string PageSize = "pragmatic.mapping.page_size";

    /// <summary>The requested limit.</summary>
    public const string Limit = "pragmatic.mapping.limit";

    /// <summary>The requested offset.</summary>
    public const string Offset = "pragmatic.mapping.offset";
}
