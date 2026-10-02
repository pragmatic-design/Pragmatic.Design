using Pragmatic.Mapping;
using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>
///     The line, for reading and writing.
/// </summary>
/// <remarks>
///     Bidirectional on purpose: it is the most common shape, and it makes both
///     <c>RequiredNavigations</c> and <c>WrittenNavigations</c> exist on this type. The case in which they
///     diverge needs a purpose-built DTO and gets its own cell.
/// </remarks>
[MapFrom<OrderLine>]
[MapTo<OrderLine>]
[GenerateProjection]
public partial record OrderLineDto
{
    /// <summary>
    ///     The key the merge pairs elements by. Without it, <c>PRAG0437</c>.
    /// </summary>
    public Guid Id { get; init; }

    public string Product { get; init; } = "";

    public int Quantity { get; init; }

    /// <summary>
    ///     Read and never written: the entity computes it, and a caller who sends it is not writing it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without the direction, <c>[MapIgnore]</c> would remove it from the response too. It is the
    ///     difference between «not mine» and «not yours to write», and the direction is what lets one
    ///     type express the second.
    /// </remarks>
    [MapIgnore(MappingDirection.ToEntity)]
    public string Display { get; init; } = "";

    /// <summary>
    ///     The second level. It is what makes <c>Lines.Allocations</c> appear in the order's navigation
    ///     lists, and makes the case two deep instead of one.
    /// </summary>
    public List<AllocationDto> Allocations { get; init; } = [];
}
