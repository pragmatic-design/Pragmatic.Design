using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The child of the <b>single navigation</b> case — the address that can be removed.
/// </summary>
/// <remarks>
///     <para>
///         The case of saying «this order no longer has a delivery address»:
///         <c>[ReferenceStrategy(ReferenceStrategy.Detach)]</c> on the property that carries it.
///     </para>
///     <para>
///         ⚠️ <c>[PartOf&lt;Order&gt;]</c> is not decoration: without it <c>PRAG0436</c> refuses the
///         shape — a nested child must be declared part of the aggregate. It is the rule that answers
///         «who may write it», and it comes before the strategy it is written with.
///     </para>
///     <para>
///         ⚠️ And <c>Exclusive = false</c>: the order writes it nested <b>and</b> it has a door of its
///         own (<c>RenameDeliveryAddressMutation</c>). «The parent may write it» and «it has no life of
///         its own» are two different things, and the full case asks for the first without the second.
///     </para>
///     <para>
///         So what happens to the <b>row</b> after the detach is decided by the relation, not by the
///         framework — the same division of labour <c>CollectionStrategy.Sync</c> has — and it is what
///         <c>TheSingleNavigation</c> measures instead of assuming.
///     </para>
/// </remarks>
[Entity]
[PartOf<Order>(Exclusive = false)]
public partial class DeliveryAddress : IEntity
{
    [Required]
    public string Street { get; private set; } = "";

    [Required]
    public string City { get; private set; } = "";
}
