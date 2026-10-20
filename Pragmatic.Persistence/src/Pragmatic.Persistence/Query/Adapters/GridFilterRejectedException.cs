namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A grid sent a filter clause the adapter cannot apply: one it cannot read, an operator the field does not
///     support, or a value the field cannot be compared with.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Dropping the clause is the defect this replaces (#318).</b> A filter is a restriction, so a clause
///         left out answers with <em>more</em> rows than the client asked for, under a success status: the grid
///         shows them as the filtered result. Inside an <c>or</c> the DevExpress adapter dropped the whole filter,
///         and every row came back.
///     </para>
///     <para>
///         The field itself is checked first: a field the adapter does not filter on is a
///         <see cref="GridFieldRejectedException" />, never this, so the reason here never says whether a withheld
///         column exists. Thrown rather than returned, like the other two refusals; the host maps it to 400.
///     </para>
/// </remarks>
public sealed class GridFilterRejectedException : InvalidOperationException
{
    /// <summary>Creates the exception for a clause and the reason it cannot be applied.</summary>
    /// <param name="field">The field the clause names, or null when the filter is malformed before a field is read.</param>
    /// <param name="rejection">Why the clause cannot be applied.</param>
    /// <param name="operator">The operator the clause names, where it has one.</param>
    public GridFilterRejectedException(string? field, GridFilterRejection rejection, string? @operator = null)
        : base(rejection switch
        {
            GridFilterRejection.UnsupportedOperator =>
                $"The operator '{@operator}' cannot be applied to the field '{field}'.",
            GridFilterRejection.UnusableValue =>
                $"The value given for the field '{field}' cannot be compared with it.",
            _ => "The grid filter is not a clause, a group of clauses or a negation.",
        })
    {
        Field = field;
        Rejection = rejection;
        Operator = @operator;
    }

    /// <summary>The field the clause names, as the grid spelled it; null for a malformed filter.</summary>
    public string? Field { get; }

    /// <summary>The operator the clause names, as the grid spelled it, where it has one.</summary>
    public string? Operator { get; }

    /// <summary>Why the clause cannot be applied.</summary>
    public GridFilterRejection Rejection { get; }
}
