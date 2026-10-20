namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Why a grid filter clause on a field the adapter does filter on cannot be applied.
/// </summary>
public enum GridFilterRejection
{
    /// <summary>
    ///     The filter is not a clause, a group or a negation: a connective that is not <c>and</c> or <c>or</c>, a
    ///     group with nothing in it, an element that is neither.
    /// </summary>
    Malformed,

    /// <summary>The operator is not one the field's type supports, or not one the adapter knows.</summary>
    UnsupportedOperator,

    /// <summary>The value is not one the field can be compared with: of another type, past its range, or missing.</summary>
    UnusableValue,
}
