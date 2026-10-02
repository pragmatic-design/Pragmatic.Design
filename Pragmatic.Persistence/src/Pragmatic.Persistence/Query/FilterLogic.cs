namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the logical operator for combining filters in a group.
/// </summary>
public enum FilterLogic
{
    /// <summary>
    ///     Combine filters with logical AND.
    ///     All conditions must be true.
    /// </summary>
    And,

    /// <summary>
    ///     Combine filters with logical OR.
    ///     At least one condition must be true.
    /// </summary>
    Or
}
