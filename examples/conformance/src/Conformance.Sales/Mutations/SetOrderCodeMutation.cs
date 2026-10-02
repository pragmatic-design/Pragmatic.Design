using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A mutation that writes a property under a <b>different name</b> from its target.
/// </summary>
/// <remarks>
///     <para>
///         The case that measures the convergence: a mutation is a <c>[MapTo&lt;TEntity&gt;]</c>
///         classified by a different attribute, so it inherits what Mapping can do.
///         <c>[MapProperty(Target = …)]</c> is the cheapest proof — a second mapper that did not read it
///         would look for <c>Order.OrderCode</c>, which does not exist.
///     </para>
///     <para>
///         ⚠️ The case <b>does not discriminate on its own</b>: a write that does not arrive and a write
///         that arrives under the wrong name fail the same way. The control is
///         <c>RenameOrderMutation</c>, which writes the same <c>Reference</c> by its direct name: if the
///         rename stopped working that one would stay green, and only this one would turn red.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/code")]
[ReturnsDto<OrderDto>]
public partial class SetOrderCodeMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    /// <summary>The name on the wire; the target is <c>Order.Reference</c>.</summary>
    [MapProperty(Target = nameof(Order.Reference))]
    public string OrderCode { get; init; } = "";
}
