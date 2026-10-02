namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     How a <c>[RollUp&lt;TChild&gt;]</c> aggregates.
/// </summary>
/// <remarks>
///     Mirrors <c>Pragmatic.Persistence.Entity.RollUpAggregation</c> member for member and value for
///     value: the attribute argument arrives as the underlying <c>int</c>, so a member reordered on one
///     side and not the other would decode as a different, valid aggregation.
/// </remarks>
internal enum RollUpAggregationKind
{
    Sum = 0,
    Count = 1
}
