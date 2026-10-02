using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>
///     A shape that declares a name the wire does not carry.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Not a shape to imitate</b>: it is here as a control case — like
///         <c>GetRawOrderQuery</c> for unreachable schemas. A hand-written DTO ends up declaring the
///         columns the projection fills, and among them are the ones the framework strips during
///         serialization.
///     </para>
///     <para>
///         What makes the case necessary: whoever strips and whoever publishes must read one list. If
///         they were two, the document would announce a field no response carries, and a client
///         generated from that document would read a silent default. The list is a single one
///         (<c>ReservedWireNames</c>, linked into the runtime and the generator) and this type keeps the
///         two ends honest.
///     </para>
///     <para>
///         ⚠️ <c>PersistenceId</c> is the <b>only</b> one of the five names that can be declared here,
///         and the reason is itself a fact about the framework: the other four — <c>RowVersion</c>,
///         <c>TenantId</c>, <c>OwnerId</c>, <c>AccessScopes</c> — do not exist on <c>Order</c>, which is
///         not <c>[ConcurrencyAware]</c>, not per tenant and has no owner, and the mapping refuses them
///         with <c>PRAG0303</c>. A DTO cannot declare a name the entity does not have, and that is the
///         check that makes this case small rather than fake.
///     </para>
///     <para>
///         <c>Reference</c> is the half that <b>must</b> be there: without a published property, «the
///         document does not announce persistenceId» would also be true of a type that is not published
///         at all.
///     </para>
/// </remarks>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderWireShapeDto
{
    public Guid Id { get; init; }

    public string Reference { get; init; } = "";

    public Guid PersistenceId { get; init; }
}
