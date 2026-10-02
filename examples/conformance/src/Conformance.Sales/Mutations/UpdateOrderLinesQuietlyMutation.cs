using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same write as <see cref="UpdateOrderLinesMutation" />, with a response that reaches nothing.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It exists to <b>isolate a variable</b>, not to add a shape. The invoker composes the
///         includes from two sources — the children's <em>write</em> list and the response DTO's
///         <em>read</em> list — and as long as the response is <c>OrderDto</c>, which reaches
///         <c>Lines.Allocations.Tags</c>, the two overlap: a defect in the write prefix would be covered
///         by the read, and the test would pass anyway.
///     </para>
///     <para>
///         With <c>OrderSummaryDto</c> the read list is empty. The only thing that can load the
///         grandchildren is <c>OrderLineDto.WrittenNavigations</c>, prefixed twice. That is how
///         <c>OneToManyDepth3.TheWriteListAlone_LoadsTheThirdLevel</c> measures what it claims to
///         measure.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/lines/quietly")]
[ReturnsDto<OrderSummaryDto>]
public partial class UpdateOrderLinesQuietlyMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    /// <summary>The complete set of lines the order must have after the operation.</summary>
    public required List<WriteOrderLineMutation> Lines { get; init; }
}
