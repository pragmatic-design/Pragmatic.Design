namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A many-to-many side: a <c>List&lt;TOther&gt;</c> whose rows are linked to this entity through a join table of two key columns and
///     no entity of its own (<c>[ManyToMany("TicketLabels", "TicketId", "LabelId")]</c>). In a lambda the collection reads as a one-to-many
///     (<c>Any</c>, <c>All</c>, <c>Count</c>, <c>SelectMany</c>); <c>Include</c> loads it through the join table; a graph save syncs the
///     links to the list (<see cref="IChangeTracking.CollectionsModified" />), and <c>LinkAsync</c> / <c>UnlinkAsync</c> write one link.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ManyToManyAttribute(string joinTable, string thisKey, string otherKey) : Attribute
{
    /// <summary>The join table.</summary>
    public string JoinTable { get; } = joinTable;

    /// <summary>The join table's column holding this entity's key.</summary>
    public string ThisKey { get; } = thisKey;

    /// <summary>The join table's column holding the other entity's key.</summary>
    public string OtherKey { get; } = otherKey;
}
