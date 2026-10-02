using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>
///     The order without its lines: a read-only shape that reaches no navigation.
/// </summary>
/// <remarks>
///     ⚠️ It exists to <b>isolate a variable</b>. A mutation's invoker composes the includes from two
///     sources: the children's <em>write</em> list and the response DTO's <em>read</em> list. As long as
///     the response is <c>OrderDto</c>, which reaches everything, a defect in the first stays invisible —
///     the second includes the same paths and the test passes anyway. Answering with this shape,
///     <c>RequiredNavigations</c> is empty and a single source remains.
/// </remarks>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderSummaryDto
{
    public Guid Id { get; init; }

    public string Reference { get; init; } = "";
}
