namespace Pragmatic.Persistence.Query;

/// <summary>
///     Specifies the comparison operator for string filter conditions.
///     Used with GridFilter properties to allow dynamic operator selection.
/// </summary>
public enum StringOperator
{
    /// <summary>
    ///     Exact equality comparison (property == value).
    /// </summary>
    Equals,

    /// <summary>
    ///     Contains comparison (property.Contains(value)).
    ///     This is the default.
    /// </summary>
    Contains,

    /// <summary>
    ///     Starts with comparison (property.StartsWith(value)).
    /// </summary>
    StartsWith,

    /// <summary>
    ///     Ends with comparison (property.EndsWith(value)).
    /// </summary>
    EndsWith,

    /// <summary>
    ///     Inequality comparison (property != value).
    /// </summary>
    NotEquals
}
