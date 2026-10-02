using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     A shipment: a domain key and a field that is not part of it.
/// </summary>
/// <remarks>
///     <para>
///         It exists for <c>MutationReturnType</c>: the three shapes of what a Create returns — the
///         entity, the technical key, the domain key — can be told apart only if the entity has something
///         beyond the key. <c>Carrier</c> is that something: it appears in the body of the <c>Entity</c>
///         shape and in neither of the other two.
///     </para>
/// </remarks>
[Entity]
public partial class Shipment : IEntity
{
    /// <summary>The domain key.</summary>
    [LogicKey]
    [Required]
    public string TrackingCode { get; private set; } = "";

    /// <summary>The field that separates the entity from its keys.</summary>
    [Required]
    public string Carrier { get; private set; } = "";
}
