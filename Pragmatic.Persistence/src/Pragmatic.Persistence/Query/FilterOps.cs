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
    ///     A bound named by the property: on a <c>[GridFilter&lt;T&gt;]</c>, a property whose name starts with
    ///     <c>Min</c> or <c>From</c> filters <c>&gt;=</c>, and one starting with <c>Max</c> or <c>To</c> filters
    ///     <c>&lt;=</c>. A pair of them is a range. Any other name is reported (<c>PRAG0703</c>), since it would filter
    ///     with equality. Read only when <c>Operators</c> is declared, and not <see cref="All" />.
    /// </summary>
    Range = 8,

    /// <summary>
    ///     All operators allowed.
    /// </summary>
    All = Equality | String | Compare | Range
}
