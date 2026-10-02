using Pragmatic.Mapping.Mutation;

namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Overrides how a collection property is written back to the entity.
/// </summary>
/// <remarks>
///     <para>
///         You should rarely need it. The strategy is derived from what the DTO already says it is: a
///         <c>[Patch&lt;T&gt;]</c> is a delta, so a collection it carries adds and updates without
///         removing (<see cref="CollectionStrategy.AddOnly"/>); a <c>[MapTo&lt;T&gt;]</c> is the whole
///         representation, so a collection it carries is the new state
///         (<see cref="CollectionStrategy.Sync"/>). A property that is not sent at all — null, or an
///         unset <c>Optional</c> — is never written, whatever the strategy.
///     </para>
///     <para>
///         This is for the case the shape cannot express: a collection that is append-only by domain
///         rule whoever sends it, or one that must never be written back at all
///         (<see cref="CollectionStrategy.Ignore"/>).
///     </para>
///     <example>
///         <code>
/// [MapTo&lt;Order&gt;]
/// public partial class UpdateOrderDto
/// {
///     // A full representation would remove the lines it omits; this order's lines are append-only.
///     [CollectionStrategy(CollectionStrategy.AddOnly)]
///     public List&lt;OrderLineDto&gt; Lines { get; init; } = [];
///
///     // Exposed for reading, never written back.
///     [CollectionStrategy(CollectionStrategy.Ignore)]
///     public List&lt;AuditEntryDto&gt; History { get; init; } = [];
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CollectionStrategyAttribute : Attribute
{
    /// <param name="strategy">How the collection is written back to the entity.</param>
    public CollectionStrategyAttribute(CollectionStrategy strategy) => Strategy = strategy;

    /// <summary>How the collection is written back to the entity.</summary>
    public CollectionStrategy Strategy { get; }
}
