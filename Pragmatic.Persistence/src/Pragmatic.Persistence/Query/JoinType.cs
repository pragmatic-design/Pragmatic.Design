namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the type of join operation.
/// </summary>
public enum JoinType
{
    /// <summary>
    ///     Inner join - only matching records from both sides.
    /// </summary>
    Inner,

    /// <summary>
    ///     Left outer join - all records from left, matching from right.
    /// </summary>
    Left,

    /// <summary>
    ///     Right outer join - all records from right, matching from left.
    /// </summary>
    Right,

    /// <summary>
    ///     Full outer join - all records from both sides.
    /// </summary>
    Full,

    /// <summary>
    ///     Cross join - cartesian product of both sides.
    /// </summary>
    /// <remarks>
    ///     Use with caution - produces M×N records.
    /// </remarks>
    Cross
}
