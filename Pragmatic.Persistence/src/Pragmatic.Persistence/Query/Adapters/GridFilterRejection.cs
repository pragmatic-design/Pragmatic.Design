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

    /// <summary>
    ///     The rows asked for are not one page: a read through <c>IReadRepository&lt;T&gt;.PageAsync</c> needs a
    ///     positive <c>Take</c> and a <c>Skip</c> that is a multiple of it. Reading the page that contains
    ///     <c>Skip</c> instead would answer with rows the grid did not ask for.
    /// </summary>
    NotAPage,
}
