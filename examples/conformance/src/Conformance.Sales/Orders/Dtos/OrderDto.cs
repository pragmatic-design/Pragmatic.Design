using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>
///     The order with its lines: the shape the responses use.
/// </summary>
/// <remarks>
///     <para>
///         Carrying <c>List&lt;OrderLineDto&gt;</c>, this DTO publishes
///         <c>RequiredNavigations = ["Lines"]</c>. The list is read where the <b>entity</b> comes back:
///         the invoker of a mutation that answers with this DTO includes it before mapping with
///         <c>FromEntity</c>, and <c>ApplyOrderPatchAction</c> passes it to <c>EnsureLoadedAsync</c>.
///     </para>
///     <para>
///         ⚠️ The projection does not use it. <c>As&lt;OrderDto&gt;()</c> is the only <c>Select</c>,
///         because a projection reaches the navigations by itself — it is a JOIN — and an
///         <c>Include</c> in front of a <c>Select</c> that changes type changes nothing
///         (<c>IncludeBeforeProjectionTests</c>).
///     </para>
/// </remarks>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderDto
{
    public Guid Id { get; init; }

    public string Reference { get; init; } = "";

    public List<OrderLineDto> Lines { get; init; } = [];
}
