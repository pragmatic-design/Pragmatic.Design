namespace Pragmatic.Persistence.Query;

/// <summary>One key of an <see cref="Ordering{T}" />: the entity's member it sorts by, and its direction.</summary>
/// <param name="Member">The member's path on the entity, as the generator wrote it (<c>StarRating</c>).</param>
/// <param name="Descending">True for a descending key.</param>
public readonly record struct OrderingKey(string Member, bool Descending);
