namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the comparison operator for filter conditions.
/// </summary>
public enum FilterOperator
{
    /// <summary>
    ///     Exact equality comparison (property == value).
    /// </summary>
    Equals,

    /// <summary>
    ///     Inequality comparison (property != value).
    /// </summary>
    NotEquals,

    /// <summary>
    ///     String contains comparison (property.Contains(value)).
    ///     This is the default for string properties.
    /// </summary>
    Contains,

    /// <summary>
    ///     String starts with comparison (property.StartsWith(value)).
    /// </summary>
    StartsWith,

    /// <summary>
    ///     String ends with comparison (property.EndsWith(value)).
    /// </summary>
    EndsWith,

    /// <summary>
    ///     Greater than comparison (property > value).
    /// </summary>
    GreaterThan,

    /// <summary>
    ///     Greater than or equal comparison (property >= value).
    /// </summary>
    GreaterOrEqual,

    /// <summary>
    ///     Less than comparison (property &lt; value).
    /// </summary>
    LessThan,

    /// <summary>
    ///     Less than or equal comparison (property &lt;= value).
    /// </summary>
    LessOrEqual,

    /// <summary>
    ///     Collection contains comparison (values.Contains(property)).
    ///     Use with IEnumerable filter property.
    /// </summary>
    In,

    /// <summary>
    ///     Range comparison (property >= min &amp;&amp; property &lt;= max).
    ///     Use with tuple or range type filter property.
    /// </summary>
    Between
}
