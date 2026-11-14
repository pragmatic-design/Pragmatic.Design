using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>Stretches by their end, the open ones (no end) last whatever the direction (#733).</summary>
[Query<StaffAssignment>]
public partial class StaffByEndQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Person { get; init; }

    [Sort(MapTo = nameof(StaffAssignment.ValidTo), DefaultDirection = SortDirection.Ascending, Nulls = NullOrder.Last)]
    public SortDirection? EndSort { get; init; }
}
