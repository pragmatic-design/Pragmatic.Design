namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Declares that a collection of <b>keys</b> chooses which rows a navigation points at.
/// </summary>
/// <remarks>
///     <para>
///         The case a list of ids exists for: picking the labels on an order, the amenities of a room
///         type — choosing rows that are not being edited. Loading them to link them is a query per
///         write that buys nothing, because a foreign key or a join row needs the key and nothing
///         else. A row nobody is editing does not have to be a shape the caller sends back.
///     </para>
///     <para>
///         Pairs with <see cref="CollectionStrategyAttribute" />, which keeps its usual meaning:
///         <c>Sync</c> makes the set of ids the set of links, <c>AddOnly</c> only adds, and
///         <c>Ignore</c> writes nothing. <c>Replace</c> clears first.
///     </para>
///     <para>
///         ⚠️ Removing a link is not deleting a row, and which one happens is the relationship's
///         business — a many-to-many drops the join row, a one-to-many orphans or cascades according
///         to its configuration. The same division of labour a merged collection of children has.
///     </para>
///     <para>
///         ⚠️ A null list means "I am not telling you about these", the same reading a null child
///         gets. An empty list is "none of them".
///     </para>
///     <example>
///         <code>
/// [MapTo&lt;RoomType&gt;]
/// public partial class UpdateRoomTypeDto
/// {
///     // The amenities this room type has, named by id: they are chosen, not edited.
///     [LinkIds(nameof(RoomType.Amenities))]
///     public List&lt;Guid&gt; AmenityIds { get; init; } = [];
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LinkIdsAttribute : Attribute
{
    /// <param name="navigation">The navigation on the entity whose links this list chooses.</param>
    public LinkIdsAttribute(string navigation) => Navigation = navigation;

    /// <summary>The navigation on the entity whose links this list chooses.</summary>
    public string Navigation { get; }

    /// <summary>
    ///     The related entity's key property. Defaults to <c>PersistenceId</c>.
    /// </summary>
    /// <remarks>
    ///     Named rather than inferred: the key is read through EF's entry, so it is a string at the
    ///     point of use anyway, and an entity whose identity is not <c>PersistenceId</c> has no other
    ///     way to say so.
    /// </remarks>
    public string Key { get; set; } = "PersistenceId";
}
