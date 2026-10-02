namespace Pragmatic.Mapping;

/// <summary>
///     What pairs two enums: the member's name, or its value.
/// </summary>
public enum EnumMatch
{
    /// <summary>
    ///     By member name, checked at compile time.
    /// </summary>
    /// <remarks>
    ///     The default, and deliberately so: two enums whose members are reordered produce no wrong
    ///     data in silence, because the pairing never depended on the order. A source member the
    ///     target does not have is <c>PRAG0328</c>, not a surprise at runtime.
    /// </remarks>
    ByName,

    /// <summary>
    ///     By underlying value — a cast.
    /// </summary>
    /// <remarks>
    ///     ⚠️ For lining up with something that already decided the numbers: a schema, a protocol, a
    ///     table someone else owns. It pairs values that no longer mean the same thing the moment
    ///     either enum is reordered, and nothing can tell: that is the cost of speaking someone
    ///     else's numbers, and it should be a decision rather than a default.
    /// </remarks>
    ByValue
}
