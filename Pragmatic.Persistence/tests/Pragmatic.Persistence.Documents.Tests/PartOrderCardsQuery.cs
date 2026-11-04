using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>The cards of the orders whose reference starts with a prefix, by quantity.</summary>
[Query<PartOrder, PartOrderCard>]
public partial class PartOrderCardsQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Reference { get; init; }

    [Sort(MapTo = nameof(PartOrder.Quantity), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? QuantitySort { get; init; }
}
