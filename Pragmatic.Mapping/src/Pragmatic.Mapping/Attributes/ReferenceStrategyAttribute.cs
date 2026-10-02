using Pragmatic.Mapping.Mutation;

namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Overrides how a single reference navigation is written back to the entity.
/// </summary>
/// <remarks>
///     <para>
///         The twin of <see cref="CollectionStrategyAttribute" /> for the child that is one rather
///         than many. Without it a reference had exactly one policy, hardcoded: update what is there,
///         build what is not, and treat a null as "not telling you about this one". That is the right
///         default — it is the same reading a scalar gets — but it left no way to say <b>remove the
///         address</b>.
///     </para>
///     <example>
///         <code>
/// [MapTo&lt;Order&gt;]
/// public partial class UpdateOrderDto
/// {
///     // Sending null here means "this order has no shipping address any more".
///     [ReferenceStrategy(ReferenceStrategy.Detach)]
///     public AddressDto? ShippingAddress { get; init; }
///
///     // Exposed for reading, never written back.
///     [ReferenceStrategy(ReferenceStrategy.Ignore)]
///     public CustomerDto? Customer { get; init; }
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ReferenceStrategyAttribute : Attribute
{
    /// <param name="strategy">How the navigation is written back to the entity.</param>
    public ReferenceStrategyAttribute(ReferenceStrategy strategy) => Strategy = strategy;

    /// <summary>How the navigation is written back to the entity.</summary>
    public ReferenceStrategy Strategy { get; }
}
