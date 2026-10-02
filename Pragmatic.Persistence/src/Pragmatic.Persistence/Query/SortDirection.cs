namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the direction of sorting.
/// </summary>
public enum SortDirection
{
    /// <summary>
    ///     Sort in ascending order (A-Z, 0-9, oldest first).
    /// </summary>
    Ascending,

    /// <summary>
    ///     Sort in descending order (Z-A, 9-0, newest first).
    /// </summary>
    Descending
}
