namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares that this entity has no life of its own: it is part of <typeparamref name="TParent" />'s
///     aggregate, and it is written through <typeparamref name="TParent" />.
/// </summary>
/// <typeparam name="TParent">The entity that owns this one.</typeparam>
/// <remarks>
///     <para>
///         An operation on the parent may carry this entity's DTOs and have them created, updated and
///         removed alongside it, in the parent's transaction. Without this attribute it may not: the
///         generator refuses at build time rather than writing a row past whatever permissions,
///         validation and events that row's own operations would have applied.
///     </para>
///     <para>
///         The relation alone cannot answer this. In the reference application <c>Invoice</c> declares
///         <c>[Relation.OneToMany&lt;LineItem&gt;]</c> and <c>Property</c> declares
///         <c>[Relation.OneToMany&lt;RoomType&gt;]</c> — the same metadata — yet a line item exists only
///         inside its invoice while a room type has its own mutations, endpoints and permissions.
///         Which of the two a relation is, is a fact about the domain, and this is where it is said.
///     </para>
///     <para>
///         It follows that a type marked this way cannot also carry a <c>[Resource]</c> or an
///         <b>exposed</b> <c>Mutation&lt;T&gt;</c> of its own — that is a contradiction between two
///         declarations, and it is reported as one.
///     </para>
///     <para>
///         ⚠️ Unless it is not one. This attribute answers two questions at once — «may the parent
///         write it?» and «does it have a life of its own?» — and they are not the same question. A
///         delivery address is written through its order <b>and</b> addressable on its own; refusing
///         that shape forced the choice between a parent that cannot write the child and a child
///         nobody can reach. <see cref="Exclusive" /> separates them.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// [PartOf&lt;Invoice&gt;]
/// public partial class LineItem
/// {
///     public string Description { get; set; } = "";
///     public decimal Amount { get; set; }
/// }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PartOfAttribute<TParent> : Attribute
    where TParent : class
{
    /// <summary>
    ///     Whether the parent is the <b>only</b> way in. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>true</c> — the historical meaning — is «no life of its own»: an operation of its own
    ///         that is exposed contradicts the declaration, and <c>PRAG0438</c> says so.
    ///     </para>
    ///     <para>
    ///         <c>false</c> keeps the half that lets the parent write it and drops the half that
    ///         forbids a door of its own. ⚠️ It does not weaken anything: both doors are operations,
    ///         with their own permissions, validation and events — the nested child is a mutation, and
    ///         so is the exposed one. What changes is only how many addresses the row has.
    ///     </para>
    /// </remarks>
    public bool Exclusive { get; set; } = true;

    /// <summary>
    ///     The navigation of the <c>[Relation.ManyToOne&lt;TParent&gt;]</c> that carries the ownership.
    ///     Required when the entity declares more than one relation to <typeparamref name="TParent" />;
    ///     otherwise the only one is taken.
    /// </summary>
    /// <remarks>
    ///     This attribute says who writes the row, not how the row is linked: the column, the
    ///     navigation and the delete behaviour are the relation's. With two relations to the same parent
    ///     — <c>Order</c> and <c>ReturnedTo</c> — only a name says which edge a part dies with.
    /// </remarks>
    public string? Via { get; set; }
}
