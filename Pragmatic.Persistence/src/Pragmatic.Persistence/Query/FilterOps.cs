namespace Pragmatic.Persistence.Query;

/// <summary>
///     Flags for specifying allowed filter operators on a property.
///     Used with [Filterable] attribute to restrict available operators.
/// </summary>
[Flags]
public enum FilterOps
{
    /// <summary>
    ///     No operators allowed.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Equality operators (Equals, NotEquals).
    /// </summary>
    Equality = 1,

    /// <summary>
    ///     String operators (Contains, StartsWith, EndsWith).
    /// </summary>
    String = 2,

    /// <summary>
    ///     Comparison operators (GreaterThan, GreaterOrEqual, LessThan, LessOrEqual).
    /// </summary>
    Compare = 4,

    /// <summary>
    ///     Range operators (In, Between).
    /// </summary>
    Range = 8,

    /// <summary>
    ///     All operators allowed.
    /// </summary>
    All = Equality | String | Compare | Range
}
